using ArquitecturaBase.Application.Models.Assistant;
using ArquitecturaBase.Application.Modules.WhatsApp.Channels;
using ArquitecturaBase.Application.Modules.WhatsApp.Models;
using ArquitecturaBase.Application.Modules.WhatsApp.Services;
using ArquitecturaBase.Application.UnitTests.TestFeatures.Modules.WhatsApp;
using ArquitecturaBase.Domain.Modules.WhatsApp;
using ArquitecturaBase.Domain.Results;
using Microsoft.Extensions.Logging.Abstractions;

namespace ArquitecturaBase.Application.UnitTests.Modules.WhatsApp.Services;

/// <summary>
/// Casos del asistente (ADR 0021, point 5): an automatic reply goes out with the business's saved texts, checked again before it
/// is queued; what does not pass, or the daily limit of replies, hands the conversation off to a person.
/// </summary>
public sealed class WhatsAppAssistantReplyTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;
    private static readonly Guid Hours = Guid.Parse("018f0000-0000-7000-8000-000000000101");
    private static readonly Guid Person = Guid.Parse("018f0000-0000-7000-8000-000000000102");
    private static readonly Guid Question = Guid.Parse("018f0000-0000-7000-8000-000000000103");
    private static readonly Guid Paused = Guid.Parse("018f0000-0000-7000-8000-000000000104");
    private static readonly Guid Drafted = Guid.Parse("018f0000-0000-7000-8000-000000000105");
    private static readonly Guid Form = Guid.Parse("018f0000-0000-7000-8000-000000000106");

    [Fact]
    public async Task An_exact_text_goes_out_as_the_business_saved_it_with_why_it_answered()
    {
        var arranged = await PendingAsync();

        var completed = await CompleteAsync(arranged, WhatsAppAssistantAgentCapabilitySource.ReplyId,
            new WhatsAppAssistantReplyPayload("exactText", "own", Hours, "Pregunta por el horario", "Pregunta el horario.", "Un texto anterior", null, null, null));

        Assert.True(completed.IsSuccess);
        var outgoing = Assert.Single(arranged.Host.Conversation.Outgoing);
        Assert.Equal(("Abrimos de 9 a 18.", WhatsAppOutgoingMessageKind.Text, WhatsAppMessageOrigin.Assistant), (outgoing.Body, outgoing.Kind, outgoing.Origin));
        var record = WhatsAppAssistantReplyRecordCodec.Parse(outgoing.AssistantReplyJson)!;
        Assert.Equal((WhatsAppAssistantActionKind.ExactText, WhatsAppAssistantCaseSource.Own, (Guid?)Hours, "Pregunta por el horario", "Pregunta el horario.", 1, 1),
            (record.Kind, record.CaseSource, record.CaseId, record.CaseTitle, record.Reason, record.Ordinal, record.Count));
        var row = Assert.Single(arranged.Host.Requests.Rows);
        Assert.Equal((WhatsAppAssistantRequestState.Sent, (Guid?)outgoing.Id), (row.State, row.OutgoingId));
        // The replies of today count from midnight of the business (New York, UTC-4 in October).
        Assert.Equal((arranged.Host.Context.Contact.Id, new DateTime(2026, 10, 17, 4, 0, 0, DateTimeKind.Utc)), arranged.History.CountedReplies);
        Assert.Equal(WhatsAppConversationMode.Assistant, arranged.Host.Context.Contact.Mode);
    }

    [Fact]
    public async Task A_question_goes_out_with_the_buttons_of_the_case_named_by_reference_and_place()
    {
        var arranged = await PendingAsync();

        await CompleteAsync(arranged, WhatsAppAssistantAgentCapabilitySource.ReplyId,
            new WhatsAppAssistantReplyPayload("askButtons", "own", Question, "Quiere registrar una visita", "Quiere registrar.", "¿Registrar?", [], null, null));

        var outgoing = Assert.Single(arranged.Host.Conversation.Outgoing);
        var row = Assert.Single(arranged.Host.Requests.Rows);
        Assert.Equal(("¿Querés registrar una visita?", WhatsAppOutgoingMessageKind.QuickReplies), (outgoing.Body, outgoing.Kind));
        Assert.Equal(["Sí", "Horario", "Hablar con alguien"], outgoing.QuickReplies.Select(reply => reply.Title));
        Assert.All(outgoing.QuickReplies.Select((reply, index) => (reply, index)), item =>
        {
            Assert.True(WhatsAppAssistantCaseButtonCodec.TryParse(item.reply.Id, out var requestId, out var place));
            Assert.Equal((row.RequestId, item.index), (requestId, place));
        });
        Assert.Equal(WhatsAppAssistantActionKind.AskButtons, WhatsAppAssistantReplyRecordCodec.Parse(outgoing.AssistantReplyJson)!.Kind);
    }

    // The question alone is the reference's own outgoing; after a text it goes in a reference of its own (the second action).
    [Theory]
    [InlineData(false)] [InlineData(true)]
    public async Task The_queued_actions_of_a_reply_pass_the_dispatch_check_of_their_own_references(bool afterText)
    {
        var arranged = await PendingAsync();
        var question = (WhatsAppAssistantAgentCapabilitySource.ReplyId, (AssistantCapabilityPayload)new WhatsAppAssistantReplyPayload("askButtons", "own",
            Question, "Quiere registrar una visita", "Quiere registrar.", "¿Registrar?", [], null, null));
        var text = (WhatsAppAssistantAgentCapabilitySource.ReplyId, (AssistantCapabilityPayload)new WhatsAppAssistantReplyPayload("exactText", "own",
            Hours, "Pregunta por el horario", "Pregunta el horario.", "Un texto anterior", null, null, null));
        Assert.True((afterText ? await CompleteManyAsync(arranged, text, question) : await CompleteManyAsync(arranged, question)).IsSuccess);

        var outgoing = arranged.Host.Conversation.Outgoing;
        Assert.Equal(WhatsAppOutgoingMessageKind.QuickReplies, outgoing[^1].Kind);
        foreach (var message in outgoing)
        {
            // The reference as the reader finds it by this outgoing, and the same check the reservation runs before Meta.
            var row = Assert.Single(arranged.Host.Requests.Rows, request => request.OutgoingId == message.Id);
            var reader = new FakeWhatsAppAssistantReader { Reference = new(row.RequestId, row.ContactId, row.SourceMessageId, row.ActorUserId,
                row.SubjectUserId, row.Kind, row.ModeRevision, row.LinkRevision, row.ContextJson, row.ContentExpiresAtUtc, row.State, row.ProposalId,
                row.DefinitionId, row.OutgoingId) };
            var result = await WhatsAppAssistantDispatchFixture.CreateGuard(arranged.Host.Context, reader).ValidateAsync(arranged.Host.Context.Contact, message, Ct);
            Assert.True(result.IsSuccess);
        }
    }

    [Fact]
    public async Task A_handoff_case_marks_the_conversation_for_a_person_and_sends_the_profile_message()
    {
        var arranged = await PendingAsync();

        await CompleteAsync(arranged, WhatsAppAssistantAgentCapabilitySource.ReplyId,
            new WhatsAppAssistantReplyPayload("handoff", "own", Person, "Pide hablar con una persona", "Pide una persona.", "Te paso", null, "case", "Pide una persona"));

        var contact = arranged.Host.Context.Contact;
        Assert.Equal((WhatsAppConversationMode.Manual, true, "case", "Pide una persona"), (contact.Mode, contact.NeedsPerson, contact.HandoffReasonCode, contact.HandoffReason));
        var outgoing = Assert.Single(arranged.Host.Conversation.Outgoing);
        Assert.True(outgoing.IsAssistantHandoff);
        Assert.Equal("Te paso con una persona del equipo.", outgoing.Body);
        var record = WhatsAppAssistantReplyRecordCodec.Parse(outgoing.AssistantReplyJson)!;
        Assert.Equal((WhatsAppAssistantActionKind.Handoff, "case", "Pide una persona"), (record.Kind, record.HandoffReasonCode, record.HandoffReason));
        Assert.Equal(WhatsAppAssistantRequestState.Sent, Assert.Single(arranged.Host.Requests.Rows).State);
    }

    // assistant_proposal@2: the actions of one reply, in the model's order, each with its place.
    [Fact]
    public async Task Several_actions_of_one_reply_go_out_in_order_with_their_place()
    {
        var arranged = await PendingAsync();

        var completed = await CompleteManyAsync(arranged,
            (WhatsAppAssistantAgentCapabilitySource.ReplyId, new WhatsAppAssistantReplyPayload("exactText", "own", Hours, "Pregunta por el horario",
                "Pregunta el horario.", "Un texto anterior", null, null, null)),
            (WhatsAppAssistantAgentCapabilitySource.ReplyId, new WhatsAppAssistantReplyPayload("askButtons", "own", Question, "Quiere registrar una visita",
                "Quiere registrar.", "¿Registrar?", [], null, null)));

        Assert.True(completed.IsSuccess);
        var outgoing = arranged.Host.Conversation.Outgoing;
        Assert.Equal(new[] { WhatsAppOutgoingMessageKind.Text, WhatsAppOutgoingMessageKind.QuickReplies }, outgoing.Select(message => message.Kind));
        Assert.Equal(new[] { (1, 2), (2, 2) }, outgoing.Select(message => WhatsAppAssistantReplyRecordCodec.Parse(message.AssistantReplyJson)!)
            .Select(record => (record.Ordinal, record.Count)));
        Assert.Equal(2, arranged.Host.Requests.Rows.Count);
        // H6: the queue sends by RequestedAtUtc and then by Id, a Guid v7 that is random within a millisecond: each action adds its place.
        Assert.True(outgoing[0].RequestedAtUtc < outgoing[1].RequestedAtUtc);
        Assert.Equal(outgoing.Select(message => message.Id), outgoing.OrderBy(message => message.RequestedAtUtc).ThenBy(message => message.Id).Select(message => message.Id));
    }

    // A form or a question about which form goes alone: mixed with the agent's actions, nothing of the reply goes out and it hands off.
    [Fact]
    public async Task A_form_mixed_with_other_actions_does_not_go_out_and_hands_off()
    {
        var arranged = await PendingAsync();

        await CompleteManyAsync(arranged,
            (WhatsAppAssistantAgentCapabilitySource.ReplyId, new WhatsAppAssistantReplyPayload("exactText", "own", Hours, "Pregunta por el horario",
                "Pregunta el horario.", "Un texto anterior", null, null, null)),
            ("forms.propose-registration", new WhatsAppAssistantNoReplyPayload("not_form")));

        var outgoing = Assert.Single(arranged.Host.Conversation.Outgoing);
        Assert.True(outgoing.IsAssistantHandoff);
        Assert.Equal("Te paso con una persona del equipo.", outgoing.Body);
        Assert.Equal((WhatsAppConversationMode.Manual, "replyNotAllowed"), (arranged.Host.Context.Contact.Mode, arranged.Host.Context.Contact.HandoffReasonCode));
    }

    [Fact]
    public async Task No_reply_sends_nothing_and_keeps_why_for_the_chat()
    {
        var arranged = await PendingAsync();

        await CompleteAsync(arranged, WhatsAppAssistantAgentCapabilitySource.ReplyId,
            new WhatsAppAssistantReplyPayload("noReply", "fallback", null, null, "Es un saludo de cierre.", null, null, null, null));

        Assert.Empty(arranged.Host.Conversation.Outgoing);
        var row = Assert.Single(arranged.Host.Requests.Rows);
        Assert.Equal(WhatsAppAssistantRequestState.NoReply, row.State);
        var record = WhatsAppAssistantReplyRecordCodec.Parse(row.OutcomeJson)!;
        Assert.Equal((WhatsAppAssistantActionKind.NoReply, WhatsAppAssistantCaseSource.Fallback, "Es un saludo de cierre."), (record.Kind, record.CaseSource, record.Reason));
        Assert.Equal(WhatsAppConversationMode.Assistant, arranged.Host.Context.Contact.Mode);
    }

    [Fact]
    public async Task A_drafted_reply_goes_out_with_the_facts_it_used()
    {
        var arranged = await PendingAsync();

        await CompleteAsync(arranged, WhatsAppAssistantAgentCapabilitySource.DraftId,
            new WhatsAppAssistantDraftPayload("own", Drafted, "Consultas generales", "Abrimos de lunes a viernes de 9 a 18.", "Pregunta el horario.", ["Horario"], []),
            Knowledge("Lunes a viernes de 9 a 18."));

        var outgoing = Assert.Single(arranged.Host.Conversation.Outgoing);
        Assert.Equal("Abrimos de lunes a viernes de 9 a 18.", outgoing.Body);
        var record = WhatsAppAssistantReplyRecordCodec.Parse(outgoing.AssistantReplyJson)!;
        Assert.Equal((WhatsAppAssistantActionKind.DraftedReply, "Horario"), (record.Kind, Assert.Single(record.Used)));
    }

    [Fact]
    public async Task A_draft_that_no_longer_fits_the_limits_is_not_sent_and_hands_off()
    {
        var arranged = await PendingAsync(Agent() with { Limits = WhatsAppAssistantAgentLimits.DefaultLimits with { MaxReplyLength = 100 } });

        await CompleteAsync(arranged, WhatsAppAssistantAgentCapabilitySource.DraftId,
            new WhatsAppAssistantDraftPayload("own", Drafted, "Consultas generales", "Abrimos de lunes a viernes de 9 a 18 y los sábados de 9 a 13, " +
                "con turnos que se piden por este mismo chat con un día de anticipación.", "Pregunta el horario.", ["Horario"], []),
            Knowledge("Lunes a viernes de 9 a 18."));

        var outgoing = Assert.Single(arranged.Host.Conversation.Outgoing);
        Assert.True(outgoing.IsAssistantHandoff);
        Assert.Equal("Te paso con una persona del equipo.", outgoing.Body);
        Assert.Equal("replyTooLong", arranged.Host.Context.Contact.HandoffReasonCode);
        Assert.Equal("replyTooLong", WhatsAppAssistantReplyRecordCodec.Parse(outgoing.AssistantReplyJson)!.HandoffReasonCode);
    }

    [Fact]
    public async Task The_daily_limit_of_replies_hands_off_instead_of_answering()
    {
        var arranged = await PendingAsync();
        arranged.History.Replies = 10;

        await CompleteAsync(arranged, WhatsAppAssistantAgentCapabilitySource.ReplyId,
            new WhatsAppAssistantReplyPayload("exactText", "own", Hours, "Pregunta por el horario", "Pregunta el horario.", "Abrimos de 9 a 18.", null, null, null));

        var outgoing = Assert.Single(arranged.Host.Conversation.Outgoing);
        Assert.True(outgoing.IsAssistantHandoff);
        Assert.Equal(("dailyReplyLimit", WhatsAppConversationMode.Manual), (arranged.Host.Context.Contact.HandoffReasonCode, arranged.Host.Context.Contact.Mode));
    }

    [Fact]
    public async Task A_case_paused_after_the_proposal_is_not_followed_and_hands_off()
    {
        var arranged = await PendingAsync();

        await CompleteAsync(arranged, WhatsAppAssistantAgentCapabilitySource.ReplyId,
            new WhatsAppAssistantReplyPayload("exactText", "own", Paused, "Caso en pausa", "Lo pidió.", "No sale.", null, null, null));

        Assert.DoesNotContain(arranged.Host.Conversation.Outgoing, outgoing => outgoing.Body == "No sale.");
        Assert.Equal("replyNotAllowed", arranged.Host.Context.Contact.HandoffReasonCode);
    }

    [Fact]
    public async Task Without_a_saved_agent_nothing_goes_out()
    {
        var arranged = await PendingAsync();
        arranged.Agents.Agent = null;

        await CompleteAsync(arranged, WhatsAppAssistantAgentCapabilitySource.ReplyId,
            new WhatsAppAssistantReplyPayload("exactText", "own", Hours, "Pregunta por el horario", "Pregunta el horario.", "Abrimos de 9 a 18.", null, null, null));

        Assert.Empty(arranged.Host.Conversation.Outgoing);
        Assert.Equal(WhatsAppAssistantRequestState.Blocked, Assert.Single(arranged.Host.Requests.Rows).State);
        Assert.Equal(WhatsAppConversationMode.Assistant, arranged.Host.Context.Contact.Mode);
    }

    [Fact]
    public async Task A_tap_on_a_fixed_text_button_is_answered_with_that_text_and_the_tap_is_marked()
    {
        var arranged = await QuestionSentAsync();
        var tap = Tap(arranged, 1);

        var work = await arranged.Dispatch.PrepareAsync(arranged.Host.Context.Contact.Id, Ct);

        Assert.Null(work.Value);
        var reply = arranged.Host.Requests.Rows.Single(row => row.SourceMessageId == tap.Id);
        Assert.Equal(WhatsAppAssistantRequestState.Sent, reply.State);
        var outgoing = arranged.Host.Conversation.Outgoing.Single(row => row.Id == reply.OutgoingId);
        Assert.Equal("Te esperamos de 9 a 18.", outgoing.Body);
        var record = WhatsAppAssistantReplyRecordCodec.Parse(outgoing.AssistantReplyJson)!;
        Assert.Equal((WhatsAppAssistantActionKind.ExactText, (Guid?)Question, "Horario"), (record.Kind, record.CaseId, record.Button));
        Assert.NotNull(tap.AssistantProcessedAtUtc);
    }

    [Fact]
    public async Task A_tap_on_a_handoff_button_passes_the_conversation_to_a_person()
    {
        var arranged = await QuestionSentAsync();
        var tap = Tap(arranged, 2);

        await arranged.Dispatch.PrepareAsync(arranged.Host.Context.Contact.Id, Ct);

        var contact = arranged.Host.Context.Contact;
        Assert.Equal((WhatsAppConversationMode.Manual, "button", "Hablar con alguien"), (contact.Mode, contact.HandoffReasonCode, contact.HandoffReason));
        var reply = arranged.Host.Requests.Rows.Single(row => row.SourceMessageId == tap.Id);
        Assert.True(arranged.Host.Conversation.Outgoing.Single(row => row.Id == reply.OutgoingId).IsAssistantHandoff);
        Assert.NotNull(tap.AssistantProcessedAtUtc);
    }

    [Fact]
    public async Task A_tap_on_a_button_that_sends_a_form_is_interpreted_like_a_message()
    {
        var arranged = await QuestionSentAsync();
        var tap = Tap(arranged, 0);
        var nowUtc = arranged.Host.Context.Clock.GetUtcNow().UtcDateTime;
        arranged.Host.Reader.Source = new(tap.Id, arranged.Host.Context.Contact.Id, "Sí", nowUtc, nowUtc.AddDays(90));
        arranged.Host.Context.Clock.Advance(WhatsAppAssistantReplyPolicy.BurstQuiet);

        var work = await arranged.Dispatch.PrepareAsync(arranged.Host.Context.Contact.Id, Ct);

        Assert.Equal("Sí", work.Value!.Operation!.Text);
        Assert.Equal(tap.Id, arranged.Host.Requests.Rows.Single(row => row.RequestId == work.Value.RequestId).SourceMessageId);
        Assert.Null(tap.AssistantProcessedAtUtc);
    }

    [Fact]
    public async Task A_tap_on_a_button_the_business_changed_since_the_question_goes_to_a_person()
    {
        var arranged = await QuestionSentAsync();
        var agent = arranged.Agents.Agent!;
        arranged.Agents.Agent = agent with { Cases = [.. agent.Cases.Select(item => item.Id != Question ? item : item with { Action = item.Action with
        {
            Buttons = [.. item.Action.Buttons!.Select((button, index) => index == 1 ? button with { Title = new("Horarios", null) } : button)],
        } })] };
        var tap = Tap(arranged, 1);

        await arranged.Dispatch.PrepareAsync(arranged.Host.Context.Contact.Id, Ct);

        Assert.Equal(("button", "Horario"), (arranged.Host.Context.Contact.HandoffReasonCode, arranged.Host.Context.Contact.HandoffReason));
        Assert.DoesNotContain(arranged.Host.Conversation.Outgoing, outgoing => outgoing.Body == "Te esperamos de 9 a 18.");
    }

    [Fact]
    public async Task A_tap_after_a_person_took_the_conversation_is_only_marked()
    {
        var arranged = await QuestionSentAsync();
        arranged.Host.Context.Contact.ChangeMode(WhatsAppConversationMode.Manual);
        var outgoingBefore = arranged.Host.Conversation.Outgoing.Count;
        var tap = Tap(arranged, 1);

        await arranged.Dispatch.PrepareAsync(arranged.Host.Context.Contact.Id, Ct);

        Assert.Equal(outgoingBefore, arranged.Host.Conversation.Outgoing.Count);
        Assert.DoesNotContain(arranged.Host.Requests.Rows, row => row.SourceMessageId == tap.Id);
        Assert.NotNull(tap.AssistantProcessedAtUtc);
    }

    [Fact]
    public async Task With_an_agent_the_turn_waits_until_the_person_stops_writing()
    {
        var arranged = await BuildAsync();
        var contactId = arranged.Host.Context.Contact.Id;
        arranged.Host.Context.Clock.Advance(TimeSpan.FromSeconds(2));

        var early = await arranged.Dispatch.PrepareAsync(contactId, Ct);
        arranged.Host.Context.Clock.Advance(TimeSpan.FromSeconds(2));
        var quiet = await arranged.Dispatch.PrepareAsync(contactId, Ct);

        Assert.Null(early.Value);
        Assert.NotNull(quiet.Value);
        Assert.Single(arranged.Host.Requests.Rows);
    }

    [Fact]
    public async Task A_long_burst_is_answered_after_fifteen_seconds_from_its_first_message()
    {
        var arranged = await BuildAsync();
        var nowUtc = arranged.Host.Context.Clock.GetUtcNow().UtcDateTime;
        arranged.History.BurstStartUtc = nowUtc - WhatsAppAssistantReplyPolicy.BurstLimit;

        var work = await arranged.Dispatch.PrepareAsync(arranged.Host.Context.Contact.Id, Ct);

        Assert.NotNull(work.Value);
    }

    [Fact]
    public async Task Without_an_agent_the_assistant_answers_at_once_as_before()
    {
        var arranged = await BuildAsync();
        arranged.Agents.Agent = null;

        var work = await arranged.Dispatch.PrepareAsync(arranged.Host.Context.Contact.Id, Ct);

        Assert.NotNull(work.Value);
    }

    [Fact]
    public void The_why_reads_back_bounded_and_a_broken_one_reads_as_none()
    {
        var used = Enumerable.Range(1, 12).Select(index => new string('a', 130) + index).ToArray();
        var json = WhatsAppAssistantReplyRecordCodec.Serialize(new(WhatsAppAssistantActionKind.DraftedReply, WhatsAppAssistantCaseSource.Own, Hours,
            "Pregunta por el horario", "Motivo con acentos: ¿qué?", used, 2, 3));

        var record = WhatsAppAssistantReplyRecordCodec.Parse(json)!;
        Assert.Equal(10, record.Used.Count);
        Assert.All(record.Used, label => Assert.Equal(120, label.Length));
        Assert.Contains("¿qué?", json, StringComparison.Ordinal);
        Assert.Contains("\"kind\":\"DraftedReply\"", json, StringComparison.Ordinal);
        Assert.Null(WhatsAppAssistantReplyRecordCodec.Parse("""{"kind":"DraftedReply"}"""));
        Assert.Null(WhatsAppAssistantReplyRecordCodec.Parse(json.Replace("\"ordinal\":2", "\"ordinal\":4", StringComparison.Ordinal)));
        Assert.Null(WhatsAppAssistantReplyRecordCodec.Parse("not json"));
    }

    [Fact]
    public void A_case_button_id_names_only_the_reference_and_the_place()
    {
        var requestId = Guid.CreateVersion7();

        Assert.True(WhatsAppAssistantCaseButtonCodec.TryParse(WhatsAppAssistantCaseButtonCodec.Encode(requestId, 2), out var parsed, out var index));
        Assert.Equal((requestId, 2), (parsed, index));
        Assert.False(WhatsAppAssistantCaseButtonCodec.TryParse("CASE_BUTTON:" + requestId.ToString("N") + ":3", out _, out _));
        Assert.False(WhatsAppAssistantCaseButtonCodec.TryParse("CASE_BUTTON:" + Guid.Empty.ToString("N") + ":0", out _, out _));
        Assert.False(WhatsAppAssistantCaseButtonCodec.TryParse("AI_FORM:" + requestId.ToString("N") + ":0", out _, out _));
        Assert.Throws<ArgumentOutOfRangeException>(() => WhatsAppAssistantCaseButtonCodec.Encode(requestId, 3));
    }

    // H5: when the engine does not answer (it failed, could not tell, expired or refused the request for good), a person does: the
    // conversation goes to Atención manual marked «Necesita una persona», with the profile's message while the person can be reached.
    [Theory]
    [InlineData("failed")]
    [InlineData("expired")]
    [InlineData("uncertain")]
    public async Task A_failure_of_the_engine_hands_off_with_the_profile_message(string state)
    {
        var arranged = await PendingAsync();

        Assert.True((await FailAsync(arranged, state)).IsSuccess);

        var contact = arranged.Host.Context.Contact;
        Assert.Equal((WhatsAppConversationMode.Manual, true, "assistantFailed", (string?)null),
            (contact.Mode, contact.NeedsPerson, contact.HandoffReasonCode, contact.HandoffReason));
        var outgoing = Assert.Single(arranged.Host.Conversation.Outgoing);
        Assert.True(outgoing.IsAssistantHandoff);
        Assert.Equal("Te paso con una persona del equipo.", outgoing.Body);
        var record = WhatsAppAssistantReplyRecordCodec.Parse(outgoing.AssistantReplyJson)!;
        Assert.Equal((WhatsAppAssistantActionKind.Handoff, "assistantFailed"), (record.Kind, record.HandoffReasonCode));
        var row = Assert.Single(arranged.Host.Requests.Rows);
        Assert.Equal((WhatsAppAssistantRequestState.Blocked, (string?)null), (row.State, row.OutcomeJson));
    }

    [Fact]
    public async Task An_engine_error_that_is_not_retried_hands_off_too()
    {
        var arranged = await PendingAsync();

        await arranged.Dispatch.CompleteAsync(arranged.Host.Context.Contact.Id, arranged.RequestId,
            Result.Failure<AssistantOperationResponse>(Error.Failure("Ai.Assistant.ProviderUnavailable", "Fictitious provider failure.")), Ct);

        Assert.Equal("assistantFailed", arranged.Host.Context.Contact.HandoffReasonCode);
        var outgoing = Assert.Single(arranged.Host.Conversation.Outgoing);
        Assert.True(outgoing.IsAssistantHandoff);
        Assert.Equal("assistantFailed", WhatsAppAssistantReplyRecordCodec.Parse(outgoing.AssistantReplyJson)!.HandoffReasonCode);
        var row = Assert.Single(arranged.Host.Requests.Rows);
        Assert.Equal((WhatsAppAssistantRequestState.Blocked, (string?)null), (row.State, row.OutcomeJson));
    }

    // The engine still had not answered when the 24-hour window closed (its worker was down): the next round of the processor finds
    // the request no longer valid and a person answers, with only the mark, since nothing can go out.
    [Fact]
    public async Task A_request_that_waits_past_the_window_hands_off_in_the_next_round_and_keeps_why_for_the_chat()
    {
        var arranged = await PendingAsync();
        var row = Assert.Single(arranged.Host.Requests.Rows);
        arranged.Host.Context.Clock.Advance(TimeSpan.FromHours(25));

        var work = await arranged.Dispatch.PrepareAsync(arranged.Host.Context.Contact.Id, Ct);

        Assert.Null(work.Value);
        Assert.Equal((WhatsAppConversationMode.Manual, "assistantFailed"), (arranged.Host.Context.Contact.Mode, arranged.Host.Context.Contact.HandoffReasonCode));
        Assert.Empty(arranged.Host.Conversation.Outgoing);
        Assert.Equal(WhatsAppAssistantRequestState.Blocked, row.State);
        var why = WhatsAppAssistantReplyRecordCodec.Parse(row.OutcomeJson)!;
        Assert.Equal((WhatsAppAssistantActionKind.Handoff, "assistantFailed"), (why.Kind, why.HandoffReasonCode));
    }

    // A newer message took its place: that one gets its own interpretation, so this failure hands nothing off.
    [Fact]
    public async Task A_failure_after_a_newer_message_does_not_hand_off()
    {
        var arranged = await PendingAsync();
        arranged.Host.Reader.Source = arranged.Host.Reader.Source! with { Id = Guid.NewGuid(), Body = "Otra consulta" };

        await FailAsync(arranged, "failed");

        Assert.Equal((WhatsAppConversationMode.Assistant, false), (arranged.Host.Context.Contact.Mode, arranged.Host.Context.Contact.NeedsPerson));
        Assert.Empty(arranged.Host.Conversation.Outgoing);
    }

    [Fact]
    public async Task Without_an_agent_a_failure_of_the_engine_only_marks_the_conversation()
    {
        var arranged = await PendingAsync();
        arranged.Agents.Agent = null;

        await FailAsync(arranged, "failed");

        Assert.True(arranged.Host.Context.Contact.NeedsPerson);
        Assert.Empty(arranged.Host.Conversation.Outgoing);
        Assert.Equal("assistantFailed", WhatsAppAssistantReplyRecordCodec.Parse(Assert.Single(arranged.Host.Requests.Rows).OutcomeJson)!.HandoffReasonCode);
    }

    [Theory]
    [InlineData("running")]
    [InlineData("retryable")]
    [InlineData("notFound")]
    public async Task What_is_pending_or_can_be_retried_does_not_hand_off(string outcome)
    {
        var arranged = await PendingAsync();

        if (outcome == "running") await FailAsync(arranged, "running");
        else await arranged.Dispatch.CompleteAsync(arranged.Host.Context.Contact.Id, arranged.RequestId, Result.Failure<AssistantOperationResponse>(outcome == "retryable"
            ? Error.Conflict("Ai.Assistant.ActiveTurn", "Fictitious active turn.", new Dictionary<string, object?> { ["retryable"] = true })
            : Error.NotFound("Ai.Assistant.NotFound", "Fictitious unknown operation.")), Ct);

        Assert.Equal((WhatsAppConversationMode.Assistant, false), (arranged.Host.Context.Contact.Mode, arranged.Host.Context.Contact.NeedsPerson));
        Assert.Equal(WhatsAppAssistantRequestState.Pending, Assert.Single(arranged.Host.Requests.Rows).State);
        Assert.Empty(arranged.Host.Conversation.Outgoing);
    }

    [Theory]
    [InlineData("person")]
    [InlineData("relinked")]
    [InlineData("capture")]
    public async Task A_failure_after_a_person_took_the_conversation_or_of_a_suggestion_after_a_capture_does_not_hand_off(string scope)
    {
        var arranged = await PendingAsync();
        var row = Assert.Single(arranged.Host.Requests.Rows);
        var contact = arranged.Host.Context.Contact;
        if (scope == "person") contact.ChangeMode(WhatsAppConversationMode.Manual);
        else if (scope == "relinked")
        {
            // The same account linked again: another link, so the request is not of this conversation.
            var user = contact.UserId!.Value;
            contact.UnlinkUser();
            contact.LinkUser(user);
        }
        else
        {
            // Nobody asked: the assistant only weighed what to suggest after a form came back.
            Assert.True(row.SetCapturedSuggestion(Guid.NewGuid(), null, null, "es", false, arranged.Host.Context.Clock.GetUtcNow().UtcDateTime));
            arranged.Host.Reader.Reference = WhatsAppAssistantSuggestionPolicy.Reference(row);
        }

        await FailAsync(arranged, "failed");

        Assert.False(arranged.Host.Context.Contact.NeedsPerson);
        Assert.Empty(arranged.Host.Conversation.Outgoing);
        Assert.Equal(WhatsAppAssistantRequestState.Blocked, row.State);
    }

    private sealed record Arranged(WhatsAppAssistantSuggestionFixture Host, WhatsAppAssistantDispatchService Dispatch, FakeWhatsAppAssistantAgentReader Agents,
        FakeWhatsAppAssistantHistoryReader History, Guid RequestId);

    private static async Task<Arranged> PendingAsync(WhatsAppAssistantAgentRow? agent = null)
    {
        var arranged = await BuildAsync(agent);
        // The person stopped writing: past the quiet time of a burst.
        arranged.Host.Context.Clock.Advance(WhatsAppAssistantReplyPolicy.BurstQuiet);
        var work = await arranged.Dispatch.PrepareAsync(arranged.Host.Context.Contact.Id, Ct);
        return arranged with { RequestId = work.Value!.RequestId };
    }

    private static async Task<Arranged> BuildAsync(WhatsAppAssistantAgentRow? agent = null)
    {
        var host = await WhatsAppAssistantSuggestionFixture.ReadyAsync();
        var agents = new FakeWhatsAppAssistantAgentReader { Agent = agent ?? Agent() };
        var history = new FakeWhatsAppAssistantHistoryReader();
        var automatic = new WhatsAppAssistantAutomaticPolicy(WhatsAppAssistantAcceptanceFixture.Create(host), host.Reader, host.Context.Clock, host.Conversation,
            host.Messages, new WhatsAppAssistantTerminalFixture().Policy, new(host.Reader, host.Context.Clock),
            WhatsAppAssistantAcceptanceFixture.CreateReply(host, agents, history));
        var dispatch = new WhatsAppAssistantDispatchService(host.Context.Policy, new(host.Reader, host.Assistant, host.Forms, host.Context.Clock, host.Presentation),
            host.Reader, host.Requests, host.Conversation, host.UnitOfWork, automatic, NullLogger<WhatsAppAssistantDispatchService>.Instance);
        host.Requests.Rows.Clear();
        host.Reader.Reference = null;
        host.Conversation.Outgoing.Clear();
        host.Context.Contact.ChangeMode(WhatsAppConversationMode.Assistant);
        return new(host, dispatch, agents, history, Guid.Empty);
    }

    private static async Task<Arranged> QuestionSentAsync()
    {
        var arranged = await PendingAsync();
        Assert.True((await CompleteAsync(arranged, WhatsAppAssistantAgentCapabilitySource.ReplyId,
            new WhatsAppAssistantReplyPayload("askButtons", "own", Question, "Quiere registrar una visita", "Quiere registrar.", "¿Registrar?", [], null, null))).IsSuccess);
        return arranged;
    }

    private static WhatsAppMessage Tap(Arranged arranged, int index)
    {
        var host = arranged.Host;
        var question = Assert.Single(host.Conversation.Outgoing, outgoing => outgoing.Kind == WhatsAppOutgoingMessageKind.QuickReplies);
        var button = question.QuickReplies[index];
        var nowUtc = host.Context.Clock.GetUtcNow().UtcDateTime;
        var message = WhatsAppMessage.Inbound(host.Context.Contact.Id, "wamid.fictitious-case-tap-" + Guid.NewGuid().ToString("N"), WhatsAppMessageKind.ButtonReply,
            button.Title, button.Id, nowUtc);
        message.Classify(WhatsAppMessagePurpose.Business);
        message.MarkProcessed(nowUtc);
        message.SetInboundSequence(3);
        host.Messages.Messages.Add(message);
        host.Reader.CaseTap = new(message.Id, message.WaMessageId, message.ReplyId!, message.OccurredAtUtc);
        return message;
    }

    private static async Task<Result> CompleteAsync(Arranged arranged, string capability, AssistantCapabilityPayload payload,
        params AssistantObservedRead[] reads)
    {
        var host = arranged.Host;
        var outcome = host.Assistant.Outcome.Value;
        var turn = Guid.NewGuid();
        host.Assistant.Outcome = outcome with
        {
            RequestId = arranged.RequestId,
            Proposal = new AssistantObservedProposal(turn, Guid.NewGuid(), new(capability, 1), payload)
                { ObservedReads = [.. reads.Select(read => read with { TurnId = turn })] },
            Provenance = outcome.Provenance! with { ChannelId = "whatsapp-assistant" },
        };
        return await arranged.Dispatch.CompleteAsync(host.Context.Contact.Id, arranged.RequestId, host.Assistant.Outcome, Ct);
    }

    private static Task<Result> FailAsync(Arranged arranged, string state)
    {
        var host = arranged.Host;
        var outcome = host.Assistant.Outcome.Value;
        host.Assistant.Outcome = outcome with
        {
            RequestId = arranged.RequestId, State = state, Proposal = null, Proposals = [],
            Provenance = outcome.Provenance! with { ChannelId = "whatsapp-assistant" },
        };
        return arranged.Dispatch.CompleteAsync(host.Context.Contact.Id, arranged.RequestId, host.Assistant.Outcome, Ct);
    }

    private static async Task<Result> CompleteManyAsync(Arranged arranged, params (string Capability, AssistantCapabilityPayload Payload)[] selected)
    {
        var host = arranged.Host;
        var outcome = host.Assistant.Outcome.Value;
        var turn = Guid.NewGuid();
        AssistantObservedProposal[] proposals = [.. selected.Select(item => new AssistantObservedProposal(turn, Guid.NewGuid(), new(item.Capability, 1), item.Payload))];
        host.Assistant.Outcome = outcome with
        {
            RequestId = arranged.RequestId, Proposal = proposals[0], Proposals = proposals,
            Provenance = outcome.Provenance! with { ChannelId = "whatsapp-assistant" },
        };
        return await arranged.Dispatch.CompleteAsync(host.Context.Contact.Id, arranged.RequestId, host.Assistant.Outcome, Ct);
    }

    private static AssistantObservedRead Knowledge(string hours) => new(Guid.Empty, 1, new(WhatsAppAssistantAgentCapabilitySource.KnowledgeId, 1),
        new WhatsAppAssistantKnowledgePayload(true, [new("hours-1", "Hours", "Horario", hours)], false));

    private static WhatsAppAssistantAgentRow Agent() => new(2, "es",
        new("Asistente", "Soy el asistente del negocio.", "Cercano.", "Responde consultas.", "No da precios.", new("Te paso con una persona del equipo.", null)),
        [
            new(Hours, "Pregunta por el horario", WhatsAppAssistantInputType.Any, true,
                new(WhatsAppAssistantActionKind.ExactText, Text: new("Abrimos de 9 a 18.", null))),
            new(Person, "Pide hablar con una persona", WhatsAppAssistantInputType.Any, true,
                new(WhatsAppAssistantActionKind.Handoff, HandoffReason: "Pide una persona")),
            new(Question, "Quiere registrar una visita", WhatsAppAssistantInputType.Any, true,
                new(WhatsAppAssistantActionKind.AskButtons, Question: new("¿Querés registrar una visita?", null),
                    Buttons: [new(new("Sí", null), WhatsAppAssistantButtonDestination.SendForm, Form, null),
                        new(new("Horario", null), WhatsAppAssistantButtonDestination.ExactText, null, new("Te esperamos de 9 a 18.", null)),
                        new(new("Hablar con alguien", null), WhatsAppAssistantButtonDestination.Handoff, null, null)])),
            new(Paused, "Caso en pausa", WhatsAppAssistantInputType.Any, false,
                new(WhatsAppAssistantActionKind.ExactText, Text: new("No sale.", null))),
            new(Drafted, "Consultas generales", WhatsAppAssistantInputType.Any, true,
                new(WhatsAppAssistantActionKind.DraftedReply, Instructions: "Respondé breve con la información cargada.")),
        ],
        new(WhatsAppAssistantActionKind.NoReply),
        WhatsAppAssistantAgentLimits.DefaultLimits);
}
