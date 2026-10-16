using ArquitecturaBase.Domain.Modules.WhatsApp;

namespace ArquitecturaBase.Domain.UnitTests.Modules.WhatsApp;

public sealed class WhatsAppFormActionOutgoingTests
{
    private static readonly DateTime Now = new(2026, 10, 16, 12, 0, 0, DateTimeKind.Utc);
    private static WhatsAppOutgoingMessage Create() => WhatsAppOutgoingMessage.CreateFormAction(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), "Visita aprobada", Now);

    [Fact]
    public void A_system_action_fixes_its_recipient_and_run_without_an_operator()
    {
        var outgoing = Create();
        Assert.Equal(WhatsAppMessageOrigin.FormAction, outgoing.Origin);
        Assert.NotNull(outgoing.RecipientUserId);
        Assert.Null(outgoing.OperatorUserId);
        Assert.Equal(WhatsAppOutgoingMessageKind.Text, outgoing.Kind);
    }

    [Fact]
    public void A_pdf_uses_the_private_document_and_never_a_public_url()
    {
        var document = new WhatsAppOutgoingDocument("private/action.pdf", "Visita.pdf", "application/pdf", 120, new string('a', 64));
        var outgoing = WhatsAppOutgoingMessage.CreateFormAction(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), "Tu PDF", Now, document);
        Assert.Equal(WhatsAppOutgoingMessageKind.Document, outgoing.Kind);
        Assert.Equal(document, outgoing.Document);
        Assert.DoesNotContain(document.StorageKey, document.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public void Expiration_before_the_message_effect_returns_to_pending_without_losing_uploaded_media()
    {
        var outgoing = Create(); var reservation = outgoing.ReserveFormAction(Now);
        Assert.True(outgoing.RecordFormActionMedia(reservation, "123456"));
        outgoing.RecoverExpiredFormAction();
        Assert.Equal(WhatsAppOutgoingMessageState.Pending, outgoing.State);
        Assert.Equal("123456", outgoing.DocumentMediaId);
        Assert.False(outgoing.MarkFormActionMessageEffect(reservation, Now));
    }

    [Fact]
    public void Expiration_after_the_message_effect_is_uncertain_and_cannot_retry()
    {
        var outgoing = Create(); var reservation = outgoing.ReserveFormAction(Now);
        Assert.True(outgoing.MarkFormActionMessageEffect(reservation, Now));
        outgoing.RecoverExpiredFormAction();
        Assert.Equal(WhatsAppOutgoingMessageState.Uncertain, outgoing.State);
        Assert.False(outgoing.RetryFormAction(Now));
        Assert.False(outgoing.MarkFormActionMessageEffect(reservation, Now));
    }

    [Fact]
    public void A_stale_reservation_cannot_start_an_effect_or_overwrite_a_new_reservation()
    {
        var outgoing = Create(); var old = outgoing.ReserveFormAction(Now);
        outgoing.RecoverExpiredFormAction(); var current = outgoing.ReserveFormAction(Now.AddMinutes(3));
        Assert.NotEqual(old, current);
        Assert.False(outgoing.MarkFormActionMessageEffect(old, Now.AddMinutes(3)));
        Assert.False(outgoing.RecordFormActionMedia(old, "123"));
        Assert.True(outgoing.MarkFormActionMessageEffect(current, Now.AddMinutes(3)));
        Assert.False(outgoing.MarkFormActionMessageEffect(current, Now.AddMinutes(3)));
    }

    [Fact]
    public void Retrying_a_rejected_document_discards_the_old_media_id_before_uploading_again()
    {
        var document = new WhatsAppOutgoingDocument("private/visit.pdf", "Visit.pdf", "application/pdf", 80, new string('a', 64));
        var outgoing = WhatsAppOutgoingMessage.CreateFormAction(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), "Tu PDF", Now, document);
        var reservation = outgoing.ReserveFormAction(Now);
        Assert.True(outgoing.RecordFormActionMedia(reservation, "123456"));
        Assert.True(outgoing.MarkFormActionMessageEffect(reservation, Now));
        outgoing.Fail("SendRejected");
        Assert.True(outgoing.RetryFormAction(Now));
        Assert.Null(outgoing.DocumentMediaId);
        Assert.Equal(document, outgoing.Document);
    }

    [Fact]
    public void Retry_only_reopens_a_confirmed_failure_and_never_an_accepted_message()
    {
        var outgoing = Create(); outgoing.Fail("WindowClosed");
        Assert.True(outgoing.RetryFormAction(Now));
        var reservation = outgoing.ReserveFormAction(Now);
        Assert.True(outgoing.MarkFormActionMessageEffect(reservation, Now));
        outgoing.Accept("wamid.confirmed");
        Assert.False(outgoing.RetryFormAction(Now));
        Assert.Equal("wamid.confirmed", outgoing.WaMessageId);
        Assert.Null(outgoing.FailureCode);
    }
}
