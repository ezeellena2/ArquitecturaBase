using ArquitecturaBase.Application.Models.Assistant;
using ArquitecturaBase.Application.Modules.WhatsApp.Channels;
using ArquitecturaBase.Application.Modules.WhatsApp.Models;
using ArquitecturaBase.Application.Modules.WhatsApp.Services;
using ArquitecturaBase.Application.UnitTests.TestFeatures.Modules.WhatsApp;
using ArquitecturaBase.Domain.Modules.WhatsApp;

namespace ArquitecturaBase.Application.UnitTests.Modules.WhatsApp.Services;

// Probar (AS3) works like a conversation with the agent: the same preloads, up to three proposals and the same checks.
public sealed class WhatsAppAssistantAgentTestPolicyTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;
    private static readonly Guid Hours = Guid.Parse("018f0000-0000-7000-8000-000000000201");

    private static WhatsAppAssistantAgentRow Agent() => new(1, "es",
        new("Asistente", "Soy el asistente del negocio.", "Cercano.", "Responde consultas.", "No da precios.", new("Te paso con una persona del equipo.", null)),
        [new(Hours, "Pregunta por el horario", WhatsAppAssistantInputType.Any, true,
            new(WhatsAppAssistantActionKind.ExactText, Text: new("Abrimos de 9 a 18.", null)))],
        new(WhatsAppAssistantActionKind.NoReply), WhatsAppAssistantAgentLimits.DefaultLimits);

    private static AssistantOperationResponse Operation(params (string Capability, AssistantCapabilityPayload Payload)[] selected)
    {
        var turn = Guid.NewGuid();
        AssistantObservedProposal[] proposals = [.. selected.Select(item => new AssistantObservedProposal(turn, Guid.NewGuid(), new(item.Capability, 1), item.Payload))];
        return new(Guid.NewGuid(), "completed", proposals[0], null, null) { Proposals = proposals };
    }

    private static readonly WhatsAppAssistantReplyPayload ExactHours = new("exactText", "own", Hours, "Pregunta por el horario", "Pregunta el horario.",
        "Un texto anterior", null, null, null);

    [Fact]
    public async Task A_test_preloads_the_agent_like_a_conversation_and_closes_on_up_to_three_proposals()
    {
        var fixture = new WhatsAppAssistantContextFixture();
        var policy = new WhatsAppAssistantAgentTestPolicy(fixture.Recipients, fixture.Settings, new FakeAssistantForms(), new WhatsAppAssistantPresentationFixture.RecordSource());
        var context = (await policy.ContextAsync(fixture.Operator.Id, fixture.Contact.Id, Ct)).Value;
        Assert.Equal(new AssistantCapabilityReference[] { new(WhatsAppAssistantAgentCapabilitySource.AgentId, 1), new("forms.assistant-candidates", 1),
            new(WhatsAppAssistantAgentCapabilitySource.HistoryId, 1) }, context.PreloadedCapabilities);
        Assert.DoesNotContain(context.AllowedCapabilities, capability => context.PreloadedCapabilities!.Contains(capability));
        Assert.Equal(new[] { new AssistantTerminalReference("assistant_proposal", 2) }, context.AllowedTerminals);
    }

    [Fact]
    public async Task Several_actions_are_shown_in_order_and_a_form_mixed_with_them_shows_the_handoff()
    {
        var fixture = new WhatsAppAssistantContextFixture();
        var policy = new WhatsAppAssistantAgentTestPolicy(fixture.Recipients, fixture.Settings, new FakeAssistantForms(), new WhatsAppAssistantPresentationFixture.RecordSource());
        var context = (await policy.ContextAsync(fixture.Operator.Id, fixture.Contact.Id, Ct)).Value;

        var replies = await policy.ActionsAsync(Operation((WhatsAppAssistantAgentCapabilitySource.ReplyId, ExactHours),
            (WhatsAppAssistantAgentCapabilitySource.ReplyId, ExactHours)), Agent(), context, Ct);
        Assert.Equal(new[] { WhatsAppAssistantActionKind.ExactText, WhatsAppAssistantActionKind.ExactText }, replies.Select(action => action.Kind));

        var mixed = await policy.ActionsAsync(Operation((WhatsAppAssistantAgentCapabilitySource.ReplyId, ExactHours),
            ("forms.propose-registration", new WhatsAppAssistantNoReplyPayload("not_form"))), Agent(), context, Ct);
        var handoff = Assert.Single(mixed);
        Assert.Equal((WhatsAppAssistantActionKind.Handoff, "replyNotAllowed", "Te paso con una persona del equipo."),
            (handoff.Kind, handoff.HandoffReasonCode, handoff.Text));
    }

    // H4: the model reads the real chat, so the engine only runs a test for someone who reads WhatsApp chats.
    [Fact]
    public async Task A_test_reads_the_real_chat_only_for_someone_who_reads_whatsapp_chats()
    {
        var fixture = new WhatsAppAssistantContextFixture();
        var policy = new WhatsAppAssistantAgentTestPolicy(fixture.Recipients, fixture.Settings, new FakeAssistantForms(), new WhatsAppAssistantPresentationFixture.RecordSource());

        var context = (await policy.ContextAsync(fixture.Operator.Id, fixture.Contact.Id, Ct)).Value;

        Assert.Equal(new[] { WhatsAppPermissions.ConfigureAssistant, WhatsAppPermissions.Read }, context.RequiredPermissions.Actor);
        Assert.Empty(context.RequiredPermissions.Subject);
    }

    // H4: why the model chose (it may quote a notice with the values of a response) and what a form would fill are values of records.
    [Fact]
    public void Without_reading_records_a_test_shows_neither_why_nor_what_a_form_would_fill()
    {
        WhatsAppAssistantAgentTestAction[] actions =
        [
            new(WhatsAppAssistantActionKind.ExactText, WhatsAppAssistantCaseSource.Own, "Pregunta por el horario", "Leyó el aviso con el total 1.234,5.",
                ["Horario"], "Abrimos de 9 a 18.", null, null, null, null),
            new(WhatsAppAssistantActionKind.DraftedReply, WhatsAppAssistantCaseSource.Fallback, null, "Repite el aviso.", [], "El total fue 1.234,5.",
                null, null, null, null),
            new(WhatsAppAssistantActionKind.SendForm, WhatsAppAssistantCaseSource.Form, "Visita", "Prellenó con el aviso.", [], null, null,
                new("Visita", [new WhatsAppAssistantAgentTestField("Total", "1.234,5")]), null, null),
        ];

        var hidden = WhatsAppAssistantAgentTestPolicy.WithoutRecordValues(actions);

        Assert.All(hidden, action => Assert.Equal("", action.Reason));
        Assert.Equal(("Pregunta por el horario", "Abrimos de 9 a 18.", "Horario"), (hidden[0].CaseTitle, hidden[0].Text, Assert.Single(hidden[0].Used)));
        Assert.Null(hidden[1].Text);
        Assert.Equal("Visita", hidden[2].Form!.Name);
        Assert.Empty(hidden[2].Form!.Fields);
    }

    [Theory]
    [InlineData(true, "Pregunta el horario.")]
    [InlineData(false, "")]
    public async Task The_why_of_an_action_goes_only_to_a_tester_who_reads_records(bool readsRecords, string reason)
    {
        var fixture = new WhatsAppAssistantContextFixture();
        var records = new ActorRecords(readsRecords ? fixture.Operator.Id : Guid.CreateVersion7());
        var policy = new WhatsAppAssistantAgentTestPolicy(fixture.Recipients, fixture.Settings, new FakeAssistantForms(), records);
        var context = (await policy.ContextAsync(fixture.Operator.Id, fixture.Contact.Id, Ct)).Value;

        var action = Assert.Single(await policy.ActionsAsync(Operation((WhatsAppAssistantAgentCapabilitySource.ReplyId, ExactHours)), Agent(), context, Ct));

        Assert.Equal((WhatsAppAssistantActionKind.ExactText, "Abrimos de 9 a 18.", reason), (action.Kind, action.Text, action.Reason));
        Assert.Equal(1, records.Reads);
    }

    // Reads records only for one account, so a test that asked for the Subject's permissions instead of the tester's would fail.
    private sealed class ActorRecords(Guid reader) : ArquitecturaBase.Application.Interfaces.Channels.IStructuredRecordSource
    {
        public int Reads { get; private set; }

        public Task<ArquitecturaBase.Application.Models.StructuredData.StructuredRecordReadResult> ReadAsync(Guid actorUserId, string culture,
            IReadOnlyCollection<Guid> sourceReceiptIds, CancellationToken cancellationToken)
        {
            Reads++;
            return Task.FromResult(new ArquitecturaBase.Application.Models.StructuredData.StructuredRecordReadResult(actorUserId == reader,
                new Dictionary<Guid, ArquitecturaBase.Application.Models.StructuredData.StructuredRecordReadReply>()));
        }
    }
}
