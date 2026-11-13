using ArquitecturaBase.Domain.Modules.WhatsApp;

namespace ArquitecturaBase.Domain.UnitTests.Modules.WhatsApp;

public sealed class WhatsAppAssistantOutgoingTests
{
    private static readonly DateTime Now = new(2026, 10, 17, 12, 0, 0, DateTimeKind.Utc);
    private static readonly Guid Recipient = Guid.NewGuid(), Contact = Guid.NewGuid(), Request = Guid.NewGuid(), Session = Guid.NewGuid();
    private const string WaId = "5491100000000";
    private static WhatsAppOutgoingMessage Text(long link = 7, string wa = WaId, long mode = 11) =>
        WhatsAppOutgoingMessage.CreateAssistantText(Recipient, Contact, Request, "Completá el formulario", Now, link, wa, mode);

    [Theory]
    [InlineData(WhatsAppOutgoingMessageKind.Text)]
    [InlineData(WhatsAppOutgoingMessageKind.Flow)]
    [InlineData(WhatsAppOutgoingMessageKind.QuickReplies)]
    public void Automatic_outgoing_captures_the_recipient_link_and_mode_without_an_operator(WhatsAppOutgoingMessageKind kind)
    {
        var outgoing = kind switch
        {
            WhatsAppOutgoingMessageKind.Flow => WhatsAppOutgoingMessage.CreateAssistantFlow(Recipient, Contact, Request, "Completá el formulario", Session, Now, 7, WaId, 11),
            WhatsAppOutgoingMessageKind.QuickReplies => WhatsAppOutgoingMessage.CreateAssistantQuickReplies(Recipient, Contact, Request, "¿Cuál querés completar?", [new("choice-1", "Visita", 13)], Now, 7, WaId, 11),
            _ => Text(),
        };
        Assert.Equal(WhatsAppMessageOrigin.Assistant, outgoing.Origin);
        Assert.Null(outgoing.OperatorUserId); Assert.Equal(Recipient, outgoing.RecipientUserId);
        Assert.Equal(Contact, outgoing.ContactId); Assert.Equal(Request, outgoing.RequestId);
        Assert.Equal(7, outgoing.RecipientLinkRevision); Assert.Equal(WaId, outgoing.RecipientWaId);
        Assert.Equal(11, outgoing.AssistantModeRevision); Assert.Equal(kind, outgoing.Kind);
        Assert.Equal(kind == WhatsAppOutgoingMessageKind.Flow ? Session : null, outgoing.FlowSessionId);
    }

    // Task 9 of the documents: up to ten options behind a visible button of up to 20 characters, each with a unique id of up to 200 and a
    // visible title of up to 24, the limits of a WhatsApp list.
    [Fact]
    public void A_list_keeps_its_button_and_up_to_ten_options_with_the_limits_of_whatsapp()
    {
        var rows = Enumerable.Range(1, 10).Select(index => new WhatsAppOutgoingQuickReply("FORM_DOCUMENT:" + index, "Formulario " + index, 0)).ToList();
        var outgoing = WhatsAppOutgoingMessage.CreateAssistantList(Recipient, Contact, Request, "No reconocí este documento.", "Elegir formulario", rows, Now, 7, WaId, 11);
        rows.Clear();
        Assert.Equal((WhatsAppOutgoingMessageKind.List, "Elegir formulario", 10, WhatsAppMessageOrigin.Assistant),
            (outgoing.Kind, outgoing.ListButton, outgoing.QuickReplies.Count, outgoing.Origin));
        Assert.Equal((Recipient, 7L, WaId, 11L), (outgoing.RecipientUserId!.Value, outgoing.RecipientLinkRevision!.Value, outgoing.RecipientWaId, outgoing.AssistantModeRevision!.Value));
    }

    [Theory]
    [InlineData(0, "Elegir formulario", "Uno")]
    [InlineData(11, "Elegir formulario", "Uno")]
    [InlineData(1, "Un botón demasiado largo", "Uno")]
    [InlineData(1, " ", "Uno")]
    [InlineData(1, "Elegir formulario", "Un título que pasa de veinticuatro")]
    public void A_list_outside_the_limits_of_whatsapp_is_refused(int count, string button, string title)
    {
        var rows = Enumerable.Range(1, count).Select(index => new WhatsAppOutgoingQuickReply("FORM_DOCUMENT:" + index, title, 0)).ToArray();
        Assert.Throws<ArgumentException>(() => WhatsAppOutgoingMessage.CreateAssistantList(Recipient, Contact, Request, "¿Qué es?", button, rows, Now, 7, WaId, 11));
    }

    // Review of task 9: a row keeps a description of up to 72 characters, the full name of a form whose title was cut, so the rows of
    // two forms that start alike stay apart; a longer one is refused.
    [Theory]
    [InlineData(72, true)]
    [InlineData(73, false)]
    public void A_list_row_keeps_a_description_of_up_to_seventy_two_characters(int length, bool allowed)
    {
        var description = new string('a', length);
        WhatsAppOutgoingQuickReply[] rows = [new("FORM_DOCUMENT:1", "Comprobante de transfer…", 0) { Description = description }, new("FORM_DOCUMENT:2", "Otro", 0)];
        if (!allowed)
        {
            Assert.Throws<ArgumentException>(() => WhatsAppOutgoingMessage.CreateAssistantList(Recipient, Contact, Request, "¿Qué es?", "Elegir formulario", rows, Now, 7, WaId, 11));
            return;
        }
        var outgoing = WhatsAppOutgoingMessage.CreateAssistantList(Recipient, Contact, Request, "¿Qué es?", "Elegir formulario", rows, Now, 7, WaId, 11);
        Assert.Equal((description, (string?)null), (outgoing.QuickReplies[0].Description, outgoing.QuickReplies[1].Description));
        Assert.DoesNotContain("Description", System.Text.Json.JsonSerializer.Serialize(outgoing.QuickReplies[1]), StringComparison.Ordinal);
    }

    [Fact]
    public void A_list_refuses_repeated_ids()
    {
        WhatsAppOutgoingQuickReply[] rows = [new("FORM_DOCUMENT:1", "Uno", 0), new("FORM_DOCUMENT:1", "Dos", 0)];
        Assert.Throws<ArgumentException>(() => WhatsAppOutgoingMessage.CreateAssistantList(Recipient, Contact, Request, "¿Qué es?", "Elegir formulario", rows, Now, 7, WaId, 11));
    }

    [Fact]
    public void Quick_replies_capture_the_actual_choices_before_the_callers_collection_changes()
    {
        var replies = new List<WhatsAppOutgoingQuickReply> { new("choice-1", "Visita", 13), new("choice-2", "Reclamo", 21) };
        var outgoing = WhatsAppOutgoingMessage.CreateAssistantQuickReplies(Recipient, Contact, Request, "Elegí un formulario", replies, Now, 7, WaId, 11);
        replies.Clear();
        Assert.Equal([new("choice-1", "Visita", 13), new WhatsAppOutgoingQuickReply("choice-2", "Reclamo", 21)], outgoing.QuickReplies);
        Assert.NotNull(outgoing.QuickRepliesJson);
        Assert.DoesNotContain("choice-1", outgoing.QuickReplies[0].ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public void An_unqualified_definition_revision_cannot_enter_the_question_outbox()
    {
        Assert.Throws<ArgumentException>(() => WhatsAppOutgoingMessage.CreateAssistantQuickReplies(Recipient, Contact, Request,
            "Elegí", [new("choice-1", "Visita", -1)], Now, 7, WaId, 11));
    }

    [Theory]
    [InlineData("empty")] [InlineData("four")] [InlineData("duplicate")] [InlineData("long-id")]
    [InlineData("blank-id")] [InlineData("long-title")] [InlineData("blank-title")] [InlineData("body")]
    public void Invalid_quick_reply_payloads_cannot_enter_the_durable_outbox(string scenario)
    {
        IReadOnlyList<WhatsAppOutgoingQuickReply> replies = scenario switch
        {
            "empty" => [], "four" => [new("1", "A", 4), new("2", "B", 4), new("3", "C", 4), new("4", "D", 4)],
            "duplicate" => [new("1", "A", 4), new("1", "B", 4)], "long-id" => [new(new string('a',257), "A", 4)],
            "blank-id" => [new(" ", "A", 4)], "long-title" => [new("1",new string('a',21), 4)],
            "blank-title" => [new("1", " ", 4)], _ => [new("1", "A", 4)],
        };
        Assert.Throws<ArgumentException>(() => WhatsAppOutgoingMessage.CreateAssistantQuickReplies(Recipient, Contact, Request,
            scenario == "body" ? new string('a',1025) : "Elegí", replies, Now, 7, WaId, 11));
    }

    [Theory]
    [InlineData(-1, WaId, 11)] [InlineData(7, "not-a-number", 11)] [InlineData(7, WaId, -1)]
    public void A_new_automatic_send_requires_a_valid_captured_recipient_and_revisions(long link, string wa, long mode) =>
        Assert.ThrowsAny<ArgumentException>(() => Text(link, wa, mode));

    [Fact]
    public void An_expired_reservation_before_the_message_effect_can_prepare_again_with_a_new_fence()
    {
        var outgoing = Text(); var first = outgoing.ReserveAssistant(Now);
        Assert.True(outgoing.OwnsAssistantReservation(first));
        Assert.Null(outgoing.MessageEffectStartedAtUtc);
        outgoing.RecoverExpiredAssistant();
        Assert.Equal(WhatsAppOutgoingMessageState.Pending, outgoing.State); Assert.Null(outgoing.StartedAtUtc);
        var second = outgoing.ReserveAssistant(Now.AddMinutes(1));
        Assert.NotEqual(first,second); Assert.False(outgoing.MarkAssistantMessageEffect(first,Now));
        Assert.True(outgoing.MarkAssistantMessageEffect(second,Now.AddMinutes(1)));
        Assert.False(outgoing.MarkAssistantMessageEffect(second,Now.AddMinutes(1)));
    }

    // H8: an assistant send goes back to the queue only with its own reservation and before its message effect started.
    [Fact]
    public void An_assistant_send_goes_back_only_with_its_reservation_and_before_its_effect()
    {
        var outgoing = Text(); var reservation = outgoing.ReserveAssistant(Now);
        Assert.False(outgoing.ReturnUnsent(Guid.NewGuid()));
        Assert.True(outgoing.ReturnUnsent(reservation));
        Assert.Equal((WhatsAppOutgoingMessageState.Pending, (Guid?)null), (outgoing.State, outgoing.DispatchReservationId));
        var started = outgoing.ReserveAssistant(Now.AddSeconds(1));
        Assert.True(outgoing.MarkAssistantMessageEffect(started,Now.AddSeconds(1)));
        Assert.False(outgoing.ReturnUnsent(started));
        Assert.Equal(WhatsAppOutgoingMessageState.Sending,outgoing.State);
    }

    // H2: the review of the chat is only for a send of the chat; the assistant's uncertainty only admits a late acceptance.
    [Fact]
    public void An_uncertain_assistant_send_is_not_reviewed_as_a_send_of_the_chat()
    {
        var outgoing = Text(); var reservation = outgoing.ReserveAssistant(Now);
        Assert.True(outgoing.MarkAssistantMessageEffect(reservation,Now));
        outgoing.RecoverExpiredAssistant();
        Assert.Throws<InvalidOperationException>(() => outgoing.Review(Guid.NewGuid(),Now));
        Assert.Equal(WhatsAppOutgoingMessageState.Uncertain,outgoing.State);
        Assert.Null(outgoing.DeliveryReviewedByUserId);
    }

    [Fact]
    public void An_expired_post_is_uncertain_and_only_a_late_acceptance_can_confirm_it()
    {
        var outgoing = Text(); var reservation = outgoing.ReserveAssistant(Now);
        Assert.True(outgoing.MarkAssistantMessageEffect(reservation,Now));
        outgoing.RecoverExpiredAssistant();
        Assert.Equal(WhatsAppOutgoingMessageState.Uncertain,outgoing.State);
        Assert.Throws<InvalidOperationException>(() => outgoing.ReserveAssistant(Now));
        Assert.False(outgoing.RetryFormAction(1,Now));
        outgoing.Accept("wamid.assistant-late");
        Assert.Equal(WhatsAppOutgoingMessageState.Accepted,outgoing.State);
        Assert.Equal("wamid.assistant-late",outgoing.WaMessageId);
    }

    [Theory]
    [InlineData(true)] [InlineData(false)]
    public void Assistant_and_existing_origins_cannot_use_each_others_reservation(bool formAction)
    {
        var outgoing = formAction ? WhatsAppOutgoingMessage.CreateFormAction(Recipient,Contact,Request,"Tu PDF",Now)
            : WhatsAppOutgoingMessage.Create(Recipient,Contact,Request,"Manual",Now);
        Assert.Throws<InvalidOperationException>(() => outgoing.ReserveAssistant(Now));
        Assert.False(outgoing.OwnsAssistantReservation(Guid.NewGuid()));
        Assert.Throws<InvalidOperationException>(() => Text().ReserveFormAction(Now));
    }
}
