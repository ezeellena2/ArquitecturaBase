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
            WhatsAppOutgoingMessageKind.QuickReplies => WhatsAppOutgoingMessage.CreateAssistantQuickReplies(Recipient, Contact, Request, "¿Cuál querés completar?", [new("choice-1", "Visita")], Now, 7, WaId, 11),
            _ => Text(),
        };
        Assert.Equal(WhatsAppMessageOrigin.Assistant, outgoing.Origin);
        Assert.Null(outgoing.OperatorUserId); Assert.Equal(Recipient, outgoing.RecipientUserId);
        Assert.Equal(Contact, outgoing.ContactId); Assert.Equal(Request, outgoing.RequestId);
        Assert.Equal(7, outgoing.RecipientLinkRevision); Assert.Equal(WaId, outgoing.RecipientWaId);
        Assert.Equal(11, outgoing.AssistantModeRevision); Assert.Equal(kind, outgoing.Kind);
        Assert.Equal(kind == WhatsAppOutgoingMessageKind.Flow ? Session : null, outgoing.FlowSessionId);
    }

    [Fact]
    public void Quick_replies_capture_the_actual_choices_before_the_callers_collection_changes()
    {
        var replies = new List<WhatsAppOutgoingQuickReply> { new("choice-1", "Visita"), new("choice-2", "Reclamo") };
        var outgoing = WhatsAppOutgoingMessage.CreateAssistantQuickReplies(Recipient, Contact, Request, "Elegí un formulario", replies, Now, 7, WaId, 11);
        replies.Clear();
        Assert.Equal([new("choice-1", "Visita"), new WhatsAppOutgoingQuickReply("choice-2", "Reclamo")], outgoing.QuickReplies);
        Assert.NotNull(outgoing.QuickRepliesJson);
        Assert.DoesNotContain("choice-1", outgoing.QuickReplies[0].ToString(), StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("empty")] [InlineData("four")] [InlineData("duplicate")] [InlineData("long-id")]
    [InlineData("blank-id")] [InlineData("long-title")] [InlineData("blank-title")] [InlineData("body")]
    public void Invalid_quick_reply_payloads_cannot_enter_the_durable_outbox(string scenario)
    {
        IReadOnlyList<WhatsAppOutgoingQuickReply> replies = scenario switch
        {
            "empty" => [], "four" => [new("1", "A"), new("2", "B"), new("3", "C"), new("4", "D")],
            "duplicate" => [new("1", "A"), new("1", "B")], "long-id" => [new(new string('a',257), "A")],
            "blank-id" => [new(" ", "A")], "long-title" => [new("1",new string('a',21))],
            "blank-title" => [new("1", " ")], _ => [new("1", "A")],
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
