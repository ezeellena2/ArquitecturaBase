using System.Text;
using System.Text.Json;
using ArquitecturaBase.Application.Interfaces.Channels;
using ArquitecturaBase.Application.Models.Assistant;
using ArquitecturaBase.Application.Modules.WhatsApp.Channels;
using ArquitecturaBase.Application.Modules.WhatsApp.Interfaces.Persistence;
using ArquitecturaBase.Application.Modules.WhatsApp.Models;
using ArquitecturaBase.Application.UnitTests.TestFeatures.Modules.WhatsApp;
using ArquitecturaBase.Domain.Modules.WhatsApp;
using Microsoft.Extensions.Time.Testing;

namespace ArquitecturaBase.Application.UnitTests.Modules.WhatsApp.Channels;

/// <summary>
/// Casos del asistente: what the model reads of the agent, the knowledge and one conversation, and the two reply proposals the
/// server checks (the saved texts go out as they are; a drafted reply only with what the business loaded).
/// </summary>
public sealed class WhatsAppAssistantAgentCapabilitySourceTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;
    private static readonly JsonSerializerOptions Web = new(JsonSerializerDefaults.Web);
    private static readonly DateTimeOffset Now = new(2026, 11, 11, 15, 0, 0, TimeSpan.Zero);
    private static readonly Guid Subject = Guid.Parse("018f0000-0000-7000-8000-0000000000a1");
    private static readonly Guid Conversation = Guid.Parse("018f0000-0000-7000-8000-0000000000c1");
    private static readonly Guid Turn = Guid.Parse("018f0000-0000-7000-8000-0000000000f1");
    private static readonly Guid Hours = Guid.Parse("018f0000-0000-7000-8000-000000000001");
    private static readonly Guid Person = Guid.Parse("018f0000-0000-7000-8000-000000000002");
    private static readonly Guid Question = Guid.Parse("018f0000-0000-7000-8000-000000000003");
    private static readonly Guid Paused = Guid.Parse("018f0000-0000-7000-8000-000000000004");
    private static readonly Guid Drafted = Guid.Parse("018f0000-0000-7000-8000-000000000005");
    private static readonly Guid Form = Guid.Parse("018f0000-0000-7000-8000-000000000006");

    [Fact]
    public void The_five_capabilities_are_closed_reads_and_proposals_without_permissions_of_their_own()
    {
        var definitions = new Host().Source.Definitions;

        Assert.Equal([WhatsAppAssistantAgentCapabilitySource.AgentId, WhatsAppAssistantAgentCapabilitySource.KnowledgeId,
            WhatsAppAssistantAgentCapabilitySource.HistoryId, WhatsAppAssistantAgentCapabilitySource.ReplyId, WhatsAppAssistantAgentCapabilitySource.DraftId],
            definitions.Select(definition => definition.Id));
        Assert.Equal([AssistantCapabilityKind.Read, AssistantCapabilityKind.Read, AssistantCapabilityKind.Read, AssistantCapabilityKind.Proposal,
            AssistantCapabilityKind.Proposal], definitions.Select(definition => definition.Kind));
        Assert.All(definitions, definition =>
        {
            Assert.Equal(1, definition.Version);
            Assert.Empty(definition.RequiredPermissions.Actor);
            Assert.Empty(definition.RequiredPermissions.Subject);
            Assert.False(definition.OfferedInHome);
            Assert.True(definition.Budget.OutputBytes <= WhatsAppAssistantAgentCapabilitySource.ResultBytes);
            using var input = JsonDocument.Parse(definition.InputSchemaJson);
            Assert.False(input.RootElement.GetProperty("additionalProperties").GetBoolean());
            Assert.DoesNotContain("uniqueItems", definition.InputSchemaJson, StringComparison.Ordinal);
            // The strict subset needs every property in «required».
            Assert.Equal(input.RootElement.GetProperty("properties").EnumerateObject().Select(property => property.Name).Order(),
                input.RootElement.GetProperty("required").EnumerateArray().Select(name => name.GetString()!).Order());
        });
    }

    [Fact]
    public async Task The_agent_read_has_the_active_cases_in_order_in_the_language_of_the_conversation()
    {
        var host = new Host();

        var read = await host.Source.ExecuteAsync(Request(WhatsAppAssistantAgentCapabilitySource.AgentId, "{}", culture: "en"), Ct);

        var agent = Assert.IsType<WhatsAppAssistantAgentPayload>(read.Value.Payload);
        Assert.Equal([Hours, Person, Question, Drafted], agent.Cases.Select(item => item.Id));
        Assert.Equal([1, 2, 3, 4], agent.Cases.Select(item => item.Order));
        Assert.Equal(["exactText", "handoff", "askButtons", "draftedReply"], agent.Cases.Select(item => item.Action));
        // A title without translation goes in the original language; the fixed texts are not in the read.
        Assert.Equal(["Yes", "Hablar con alguien"], agent.Cases[2].Buttons!.Select(button => button.Title));
        Assert.Equal(["sendForm", "handoff"], agent.Cases[2].Buttons!.Select(button => button.Destination));
        Assert.DoesNotContain("Abrimos de 9 a 18", JsonSerializer.Serialize(agent, Web), StringComparison.Ordinal);
        Assert.Equal("noReply", agent.Fallback.Action);
        Assert.Equal(["precios"], agent.BlockedTopics);
        Assert.Equal(400, agent.MaxReplyLength);
    }

    [Fact]
    public async Task Reads_take_no_arguments_and_need_a_saved_agent()
    {
        var host = new Host();
        var withArguments = await host.Source.ExecuteAsync(Request(WhatsAppAssistantAgentCapabilitySource.AgentId, """{"all":true}"""), Ct);
        var unknown = await host.Source.ExecuteAsync(Request("whatsapp.other", "{}"), Ct);
        var withoutAgent = await new Host(agent: null).Source.ExecuteAsync(Request(WhatsAppAssistantAgentCapabilitySource.KnowledgeId, "{}"), Ct);

        Assert.Equal("WhatsApp.Assistant.InvalidProposal", withArguments.Error.Code);
        Assert.Equal("WhatsApp.Assistant.InvalidProposal", unknown.Error.Code);
        Assert.Equal("WhatsApp.Assistant.Unavailable", withoutAgent.Error.Code);
    }

    [Fact]
    public async Task The_knowledge_read_has_only_the_confirmed_facts_with_their_ids()
    {
        var host = new Host();

        var read = await host.Source.ExecuteAsync(Request(WhatsAppAssistantAgentCapabilitySource.KnowledgeId, "{}"), Ct);

        var knowledge = Assert.IsType<WhatsAppAssistantKnowledgePayload>(read.Value.Payload);
        Assert.True(knowledge.Available);
        Assert.False(knowledge.Truncated);
        Assert.Equal(["details-web", "hours-1"], knowledge.Facts.Select(fact => fact.Id));
        Assert.Equal(["Details", "Hours"], knowledge.Facts.Select(fact => fact.Zone));
    }

    [Fact]
    public async Task An_exact_text_goes_out_as_the_business_saved_it_never_as_the_model_wrote_it()
    {
        var host = new Host();

        var reply = await host.Source.ExecuteAsync(Request(WhatsAppAssistantAgentCapabilitySource.ReplyId,
            Reply("own", Hours, "exactText", null, "Pregunta a qué hora abren.")), Ct);

        Assert.Equal("complete", reply.Value.State);
        var payload = Assert.IsType<WhatsAppAssistantReplyPayload>(reply.Value.Payload);
        Assert.Equal(("exactText", "own", (Guid?)Hours, "Pregunta por el horario"), (payload.Kind, payload.CaseSource, payload.CaseId, payload.CaseTitle));
        Assert.Equal("Abrimos de 9 a 18.", payload.Text);
        Assert.Equal("Pregunta a qué hora abren.", payload.Reason);
        Assert.Null(payload.Buttons);
    }

    [Fact]
    public async Task Ask_buttons_goes_out_with_the_saved_question_and_its_buttons()
    {
        var host = new Host();

        var reply = await host.Source.ExecuteAsync(Request(WhatsAppAssistantAgentCapabilitySource.ReplyId,
            Reply("own", Question, "askButtons", null, "Quiere registrar una visita."), culture: "en"), Ct);

        var payload = Assert.IsType<WhatsAppAssistantReplyPayload>(reply.Value.Payload);
        Assert.Equal("Do you want to register a visit?", payload.Text);
        Assert.Equal(["Yes", "Hablar con alguien"], payload.Buttons!.Select(button => button.Title));
        Assert.Equal(Form, payload.Buttons![0].FormDefinitionId);
    }

    [Fact]
    public async Task A_handoff_case_goes_out_with_the_profile_message_and_the_reason_of_the_case()
    {
        var host = new Host();

        var reply = await host.Source.ExecuteAsync(Request(WhatsAppAssistantAgentCapabilitySource.ReplyId,
            Reply("own", Person, "handoff", "case", "Pide hablar con alguien."), culture: "en"), Ct);

        var payload = Assert.IsType<WhatsAppAssistantReplyPayload>(reply.Value.Payload);
        // Without an English text, the fixed message goes in the original language.
        Assert.Equal("Te paso con una persona del equipo.", payload.Text);
        Assert.Equal(("case", "Pide una persona"), (payload.HandoffReasonCode, payload.HandoffReason));
    }

    [Theory]
    [InlineData("blockedTopic")]
    [InlineData("unknown")]
    public async Task The_system_hands_off_from_any_case_on_a_blocked_topic_or_without_an_answer(string reason)
    {
        var host = new Host();

        var reply = await host.Source.ExecuteAsync(Request(WhatsAppAssistantAgentCapabilitySource.ReplyId,
            Reply("fallback", null, "handoff", reason, "No hay información de envíos.")), Ct);

        var payload = Assert.IsType<WhatsAppAssistantReplyPayload>(reply.Value.Payload);
        Assert.Equal(("handoff", "fallback", (Guid?)null), (payload.Kind, payload.CaseSource, payload.CaseId));
        Assert.Equal((reason, (string?)null), (payload.HandoffReasonCode, payload.HandoffReason));
        Assert.Equal("Te paso con una persona del equipo.", payload.Text);
    }

    [Theory]
    [InlineData("own", "018f0000-0000-7000-8000-000000000001", "handoff", "case")]
    [InlineData("own", "018f0000-0000-7000-8000-000000000002", "exactText", null)]
    [InlineData("own", "018f0000-0000-7000-8000-000000000004", "exactText", null)]
    [InlineData("own", "018f0000-0000-7000-8000-0000000000ff", "exactText", null)]
    [InlineData("own", null, "exactText", null)]
    [InlineData("fallback", "018f0000-0000-7000-8000-000000000001", "exactText", null)]
    [InlineData("fallback", null, "exactText", null)]
    [InlineData("own", "018f0000-0000-7000-8000-000000000001", "exactText", "unknown")]
    public async Task A_reply_follows_the_kind_of_an_active_case_it_names(string source, string? caseId, string kind, string? handoffReason)
    {
        var host = new Host();

        var reply = await host.Source.ExecuteAsync(Request(WhatsAppAssistantAgentCapabilitySource.ReplyId,
            Reply(source, caseId is null ? null : Guid.Parse(caseId), kind, handoffReason, "Motivo.")), Ct);

        Assert.True(reply.IsFailure);
        Assert.Equal("WhatsApp.Assistant.InvalidProposal", reply.Error.Code);
    }

    [Fact]
    public async Task No_reply_follows_only_a_case_that_says_not_to_reply()
    {
        var host = new Host();

        var reply = await host.Source.ExecuteAsync(Request(WhatsAppAssistantAgentCapabilitySource.ReplyId,
            Reply("fallback", null, "noReply", null, "Es un saludo de cierre.")), Ct);

        var payload = Assert.IsType<WhatsAppAssistantReplyPayload>(reply.Value.Payload);
        Assert.Equal(("noReply", (string?)null), (payload.Kind, payload.Text));
    }

    [Fact]
    public async Task Proposals_reject_unknown_or_repeated_properties()
    {
        var host = new Host();
        var extra = await host.Source.ExecuteAsync(Request(WhatsAppAssistantAgentCapabilitySource.ReplyId,
            """{"caseSource":"fallback","caseId":null,"kind":"noReply","handoffReason":null,"reason":"Motivo.","text":"Hola"}"""), Ct);
        var repeated = await host.Source.ExecuteAsync(Request(WhatsAppAssistantAgentCapabilitySource.ReplyId,
            """{"caseSource":"fallback","caseId":null,"kind":"noReply","kind":"exactText","handoffReason":null,"reason":"Motivo."}"""), Ct);

        Assert.Equal("WhatsApp.Assistant.InvalidProposal", extra.Error.Code);
        Assert.Equal("WhatsApp.Assistant.InvalidProposal", repeated.Error.Code);
    }

    [Fact]
    public async Task A_drafted_reply_names_the_facts_it_used_and_passes_with_what_the_business_loaded()
    {
        var host = new Host();

        var draft = await host.Source.ExecuteAsync(Request(WhatsAppAssistantAgentCapabilitySource.DraftId,
            Draft(Drafted, "Abrimos de lunes a viernes de 9 a 18, te esperamos en www.ejemplo.com.ar.", "hours-1", "details-web")), Ct);

        Assert.Equal("complete", draft.Value.State);
        var payload = Assert.IsType<WhatsAppAssistantDraftPayload>(draft.Value.Payload);
        Assert.Equal(["Horario", "Sitio web"], payload.Used);
        Assert.Empty(payload.Problems);
        Assert.Equal("Responde consultas generales", payload.CaseTitle);
    }

    [Fact]
    public async Task A_drafted_reply_with_a_link_or_price_that_was_not_loaded_comes_back_with_its_problems()
    {
        var host = new Host();

        var draft = await host.Source.ExecuteAsync(Request(WhatsAppAssistantAgentCapabilitySource.DraftId,
            Draft(Drafted, "Reservá en https://turnos.otro.com por $ 5000 la visita.", "hours-1")), Ct);

        Assert.Equal("rejected", draft.Value.State);
        var payload = Assert.IsType<WhatsAppAssistantDraftPayload>(draft.Value.Payload);
        Assert.Equal(["link", "price"], payload.Problems);
    }

    [Fact]
    public async Task A_drafted_reply_uses_the_facts_the_model_read_in_this_turn()
    {
        var host = new Host();
        var observed = new WhatsAppAssistantKnowledgePayload(true, [new("hours-2", "Hours", "Horario de verano", "De 8 a 14.")], false);
        var reads = new[] { new AssistantObservedRead(Turn, 1, new(WhatsAppAssistantAgentCapabilitySource.KnowledgeId, 1), observed) };

        var draft = await host.Source.ExecuteAsync(Request(WhatsAppAssistantAgentCapabilitySource.DraftId,
            Draft(Drafted, "En verano abrimos de 8 a 14.", "hours-2"), reads: reads), Ct);
        var current = await host.Source.ExecuteAsync(Request(WhatsAppAssistantAgentCapabilitySource.DraftId,
            Draft(Drafted, "En verano abrimos de 8 a 14.", "hours-1"), reads: reads), Ct);

        Assert.Equal(["Horario de verano"], Assert.IsType<WhatsAppAssistantDraftPayload>(draft.Value.Payload).Used);
        Assert.Equal(0, host.Knowledge.Reads);
        Assert.Equal("WhatsApp.Assistant.InvalidProposal", current.Error.Code);
    }

    [Fact]
    public async Task A_drafted_reply_needs_a_drafted_reply_case_and_facts_that_exist()
    {
        var host = new Host();
        var otherCase = await host.Source.ExecuteAsync(Request(WhatsAppAssistantAgentCapabilitySource.DraftId,
            Draft(Hours, "Abrimos de 9 a 18.", "hours-1")), Ct);
        var missingFact = await host.Source.ExecuteAsync(Request(WhatsAppAssistantAgentCapabilitySource.DraftId,
            Draft(Drafted, "Abrimos de 9 a 18.", "hours-9")), Ct);
        var repeatedFact = await host.Source.ExecuteAsync(Request(WhatsAppAssistantAgentCapabilitySource.DraftId,
            Draft(Drafted, "Abrimos de 9 a 18.", "hours-1", "hours-1")), Ct);

        Assert.Equal("WhatsApp.Assistant.InvalidProposal", otherCase.Error.Code);
        Assert.Equal("WhatsApp.Assistant.InvalidProposal", missingFact.Error.Code);
        Assert.Equal("WhatsApp.Assistant.InvalidProposal", repeatedFact.Error.Code);
    }

    [Fact]
    public async Task The_history_reads_one_conversation_in_business_time_with_the_replies_of_today()
    {
        var host = new Host();
        host.History.Messages.AddRange([
            new(Now.UtcDateTime.AddHours(-1), WhatsAppMessageDirection.Inbound, WhatsAppMessageOrigin.Conversation, WhatsAppMessageKind.Text, "¿A qué hora abren?"),
            new(Now.UtcDateTime.AddMinutes(-59), WhatsAppMessageDirection.Outbound, WhatsAppMessageOrigin.Assistant, WhatsAppMessageKind.Text, "Abrimos de 9 a 18."),
            new(Now.UtcDateTime.AddMinutes(-58), WhatsAppMessageDirection.Outbound, WhatsAppMessageOrigin.Conversation, WhatsAppMessageKind.Text, "Te escribe Ana.")]);
        host.History.Actions.AddRange([
            new(Now.UtcDateTime.AddHours(-2), WhatsAppOutgoingMessageKind.Flow, WhatsAppOutgoingMessageState.Accepted, """{"es":"Registrar visita","en":"Register visit"}""",
                "es", WhatsAppFlowCaptureState.Completed, Now.UtcDateTime.AddHours(20), false, null),
            new(Now.UtcDateTime.AddMinutes(-59), WhatsAppOutgoingMessageKind.Text, WhatsAppOutgoingMessageState.Accepted, null, null, null, null, false, "{}")]);
        host.History.Replies = 4;

        var read = await host.Source.ExecuteAsync(Request(WhatsAppAssistantAgentCapabilitySource.HistoryId, "{}"), Ct);

        var history = Assert.IsType<WhatsAppAssistantHistoryPayload>(read.Value.Payload);
        Assert.Equal((Subject, (Guid?)Conversation), host.History.Asked);
        Assert.Equal(Now.UtcDateTime.AddHours(-24), host.History.MessagesSinceUtc);
        // The business's day started at midnight in Buenos Aires: 03:00 UTC.
        Assert.Equal(new DateTime(2026, 11, 11, 3, 0, 0, DateTimeKind.Utc), host.History.RepliesSinceUtc);
        Assert.Equal(["person", "assistant", "operator"], history.Messages.Select(message => message.From));
        Assert.Equal(TimeSpan.FromHours(-3), history.Messages[0].At.Offset);
        Assert.Equal(11, history.Messages[0].At.Hour);
        Assert.Equal([("form", "Registrar visita", "completed"), ("reply", (string?)null, "sent")],
            history.Actions.Select(action => (action.Kind, action.Name, action.Outcome)));
        Assert.Equal((4, 10), (history.RepliesToday, history.DailyReplyLimit));
        Assert.Equal(Subject, history.Offers.SubjectUserId);
    }

    [Fact]
    public async Task A_long_history_keeps_the_newest_messages_within_one_read()
    {
        var host = new Host();
        var face = char.ConvertFromUtf32(0x1F600);
        for (var index = 0; index < 10; index++)
            host.History.Messages.Add(new(Now.UtcDateTime.AddMinutes(index - 30), WhatsAppMessageDirection.Inbound, WhatsAppMessageOrigin.Conversation,
                WhatsAppMessageKind.Text, string.Concat(Enumerable.Repeat(face, 300)) + index));

        var read = await host.Source.ExecuteAsync(Request(WhatsAppAssistantAgentCapabilitySource.HistoryId, "{}"), Ct);

        var history = Assert.IsType<WhatsAppAssistantHistoryPayload>(read.Value.Payload);
        var bytes = Encoding.UTF8.GetByteCount(JsonSerializer.Serialize(new { state = "complete", payload = (object)history }, Web));
        Assert.True(bytes <= WhatsAppAssistantAgentCapabilitySource.ResultBytes, $"{bytes} bytes");
        Assert.InRange(history.Messages.Count, 1, 9);
        // The newest one stays, cut without splitting a character.
        Assert.Equal(Now.AddMinutes(-21), history.Messages[^1].At);
        Assert.Equal(string.Concat(Enumerable.Repeat(face, 249)) + "…", history.Messages[^1].Text);
        Assert.Equal(history.Messages.OrderBy(message => message.At).Select(message => message.At), history.Messages.Select(message => message.At));
    }

    [Fact]
    public async Task Without_a_linked_conversation_the_history_has_only_the_offers()
    {
        var host = new Host();
        host.History.Contact = null;
        host.References.Offers.Add(new(Guid.NewGuid(), Form, Guid.NewGuid(), "Registrar visita"));
        host.References.DeclinedDefinitions.Add(Form);

        var read = await host.Source.ExecuteAsync(Request(WhatsAppAssistantAgentCapabilitySource.HistoryId, "{}"), Ct);

        var history = Assert.IsType<WhatsAppAssistantHistoryPayload>(read.Value.Payload);
        Assert.Empty(history.Messages);
        Assert.Equal(0, history.RepliesToday);
        Assert.Equal("Registrar visita", Assert.Single(history.Offers.Offers).Name);
        Assert.Equal([Form], history.Offers.DeclinedDefinitionIds);
    }

    [Fact]
    public async Task Every_payload_roundtrips_through_the_owner_codec_and_nothing_else_does()
    {
        var host = new Host();
        var outputs = new List<(string Id, AssistantCapabilityPayload Payload)>();
        foreach (var (id, json) in new[]
        {
            (WhatsAppAssistantAgentCapabilitySource.AgentId, "{}"), (WhatsAppAssistantAgentCapabilitySource.KnowledgeId, "{}"),
            (WhatsAppAssistantAgentCapabilitySource.HistoryId, "{}"),
            (WhatsAppAssistantAgentCapabilitySource.ReplyId, Reply("own", Question, "askButtons", null, "Motivo.")),
            (WhatsAppAssistantAgentCapabilitySource.DraftId, Draft(Drafted, "Abrimos de 9 a 18.", "hours-1")),
        })
            outputs.Add((id, (await host.Source.ExecuteAsync(Request(id, json), Ct)).Value.Payload));

        foreach (var (id, payload) in outputs)
        {
            var json = JsonSerializer.Serialize(payload, payload.GetType(), Web);
            var parsed = host.Source.ParseOutput(new(id, 1), json);
            Assert.True(parsed.IsSuccess, id);
            Assert.Equal(json, JsonSerializer.Serialize(parsed.Value, parsed.Value.GetType(), Web));
            Assert.True(host.Source.ParseOutput(new(id, 1), json[..^1] + ""","extra":1}""").IsFailure, id);
            Assert.True(host.Source.ParseOutput(new(id, 2), json).IsFailure, id);
        }

        Assert.True(host.Source.ParseOutput(new(WhatsAppAssistantAgentCapabilitySource.ReplyId, 1),
            """{"kind":"send","caseSource":"own","caseId":null,"caseTitle":null,"reason":"x","text":null,"buttons":null,"handoffReasonCode":null,"handoffReason":null}""").IsFailure);
    }

    private static AssistantCapabilityRequest Request(string id, string json, string culture = "es", IReadOnlyList<AssistantObservedRead>? reads = null) =>
        new(Turn, new AssistantChannelContext("whatsapp-assistant", Subject, Subject, culture, "America/Argentina/Buenos_Aires", new([], []), [], [])
            { ChannelConversationId = Conversation }, new(id, 1), json, reads ?? []);

    private static string Reply(string source, Guid? caseId, string kind, string? handoffReason, string reason) =>
        JsonSerializer.Serialize(new { caseSource = source, caseId, kind, handoffReason, reason });

    private static string Draft(Guid caseId, string text, params string[] usedFactIds) =>
        JsonSerializer.Serialize(new { caseSource = "own", caseId, text, reason = "Pregunta por el horario.", usedFactIds });

    private static WhatsAppAssistantAgentRow Agent() => new(3, "es",
        new("Asistente", "Soy el asistente del negocio.", "Cercano.", "Responde consultas.", "No da precios.", new("Te paso con una persona del equipo.", null)),
        [
            new(Hours, "Pregunta por el horario", WhatsAppAssistantInputType.Any, true,
                new(WhatsAppAssistantActionKind.ExactText, Text: new("Abrimos de 9 a 18.", null))),
            new(Person, "Pide hablar con una persona", WhatsAppAssistantInputType.Any, true,
                new(WhatsAppAssistantActionKind.Handoff, HandoffReason: "Pide una persona")),
            new(Question, "Quiere registrar una visita", WhatsAppAssistantInputType.Text, true,
                new(WhatsAppAssistantActionKind.AskButtons, Question: new("¿Querés registrar una visita?", "Do you want to register a visit?"),
                    Buttons: [new(new("Sí", "Yes"), WhatsAppAssistantButtonDestination.SendForm, Form, null),
                        new(new("Hablar con alguien", null), WhatsAppAssistantButtonDestination.Handoff, null, null)])),
            new(Paused, "Caso en pausa", WhatsAppAssistantInputType.Any, false,
                new(WhatsAppAssistantActionKind.ExactText, Text: new("No sale.", null))),
            new(Drafted, "Responde consultas generales", WhatsAppAssistantInputType.Any, true,
                new(WhatsAppAssistantActionKind.DraftedReply, Instructions: "Respondé breve con la información cargada.")),
        ],
        new(WhatsAppAssistantActionKind.NoReply),
        WhatsAppAssistantAgentLimits.DefaultLimits with { BlockedTopics = ["precios"] });

    private static BusinessKnowledge Knowledge() => new(BusinessKnowledgeAvailability.Available, 2,
    [
        new(BusinessKnowledgeSectionKey.Details, BusinessKnowledgeSectionState.Loaded, [new("details-web", "Sitio web", "www.ejemplo.com.ar")]),
        new(BusinessKnowledgeSectionKey.Hours, BusinessKnowledgeSectionState.Loaded, [new("hours-1", "Horario", "Lunes a viernes de 9 a 18.")]),
        new(BusinessKnowledgeSectionKey.Answers, BusinessKnowledgeSectionState.Empty, []),
    ], 1);

    private sealed class Host
    {
        public Host() : this(Agent()) { }

        public Host(WhatsAppAssistantAgentRow? agent)
        {
            Knowledge = new KnowledgeSource(WhatsAppAssistantAgentCapabilitySourceTests.Knowledge());
            Source = new(new Agents(agent), Knowledge, History, References, new FakeTimeProvider(Now));
        }

        public KnowledgeSource Knowledge { get; }
        public HistoryReader History { get; } = new();
        public FakeWhatsAppAssistantReader References { get; } = new();
        public WhatsAppAssistantAgentCapabilitySource Source { get; }
    }

    private sealed class Agents(WhatsAppAssistantAgentRow? agent) : IWhatsAppAssistantAgentReader
    {
        public Task<WhatsAppAssistantAgentRow?> FindMainAsync(CancellationToken cancellationToken) => Task.FromResult(agent);

        public Task<IReadOnlyList<WhatsAppAssistantTestRecipientRow>> ListTestRecipientsAsync(string? search, int limit, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<WhatsAppAssistantTestRecipientRow>>([]);
    }

    private sealed class KnowledgeSource(BusinessKnowledge knowledge) : IBusinessKnowledgeSource
    {
        public int Reads { get; private set; }

        public Task<BusinessKnowledge> ReadAsync(string culture, CancellationToken cancellationToken)
        {
            Reads++;
            return Task.FromResult(knowledge);
        }
    }

    private sealed class HistoryReader : IWhatsAppAssistantHistoryReader
    {
        public Guid? Contact { get; set; } = Guid.Parse("018f0000-0000-7000-8000-0000000000d1");
        public (Guid Subject, Guid? Contact)? Asked { get; private set; }
        public List<WhatsAppAssistantHistoryMessageRow> Messages { get; } = [];
        public List<WhatsAppAssistantHistoryActionRow> Actions { get; } = [];
        public int Replies { get; set; }
        public DateTime? MessagesSinceUtc { get; private set; }
        public DateTime? RepliesSinceUtc { get; private set; }

        public Task<Guid?> FindConversationAsync(Guid subjectUserId, Guid? contactId, CancellationToken cancellationToken)
        {
            Asked = (subjectUserId, contactId);
            return Task.FromResult(Contact);
        }

        public Task<IReadOnlyList<WhatsAppAssistantHistoryMessageRow>> ListMessagesAsync(Guid contactId, DateTime sinceUtc, int limit, CancellationToken cancellationToken)
        {
            MessagesSinceUtc = sinceUtc;
            return Task.FromResult<IReadOnlyList<WhatsAppAssistantHistoryMessageRow>>([.. Messages.TakeLast(limit)]);
        }

        public Task<IReadOnlyList<WhatsAppAssistantHistoryActionRow>> ListActionsAsync(Guid contactId, DateTime sinceUtc, int limit, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<WhatsAppAssistantHistoryActionRow>>([.. Actions.TakeLast(limit)]);

        public Task<int> CountRepliesAsync(Guid contactId, DateTime sinceUtc, CancellationToken cancellationToken)
        {
            RepliesSinceUtc = sinceUtc;
            return Task.FromResult(Replies);
        }

        public Task<DateTime?> FindBurstStartAsync(Guid contactId, CancellationToken cancellationToken) => Task.FromResult<DateTime?>(null);
    }
}
