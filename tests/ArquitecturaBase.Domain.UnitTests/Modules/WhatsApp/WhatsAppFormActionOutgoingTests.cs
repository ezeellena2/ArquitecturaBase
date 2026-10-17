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
        Assert.False(outgoing.RetryFormAction(2,Now));
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
        Assert.True(outgoing.RetryFormAction(2,Now));
        Assert.Null(outgoing.DocumentMediaId);
        Assert.Equal(document, outgoing.Document);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void Human_review_preserves_its_evidence_without_repeating_the_message(bool wasSent)
    {
        var outgoing=Create();var actor=Guid.NewGuid();var reservation=outgoing.ReserveFormAction(Now);
        outgoing.MarkFormActionMessageEffect(reservation,Now);outgoing.MarkUncertain();
        Assert.True(outgoing.ReviewFormAction(1,wasSent,actor,Now.AddMinutes(1)));
        Assert.Equal(wasSent?WhatsAppOutgoingMessageState.Reviewed:WhatsAppOutgoingMessageState.Failed,outgoing.State);
        Assert.Equal(wasSent,outgoing.DeliveryReviewWasSent);Assert.Equal(actor,outgoing.DeliveryReviewedByUserId);
        Assert.Equal(Now.AddMinutes(1),outgoing.DeliveryReviewedAtUtc);Assert.Null(outgoing.WaMessageId);
        Assert.Equal(!wasSent,outgoing.RetryFormAction(2,Now.AddMinutes(2)));
        Assert.Equal(actor,outgoing.DeliveryReviewedByUserId);
    }
    [Fact]
    public void A_late_provider_acceptance_overrides_a_human_not_sent_review_without_changing_manual_failures()
    {
        var outgoing=Create();var reservation=outgoing.ReserveFormAction(Now);outgoing.MarkFormActionMessageEffect(reservation,Now);outgoing.MarkUncertain();
        outgoing.ReviewFormAction(1,false,Guid.NewGuid(),Now.AddMinutes(1));outgoing.Accept("wamid.late");
        Assert.Equal(WhatsAppOutgoingMessageState.Accepted,outgoing.State);Assert.Equal("wamid.late",outgoing.WaMessageId);
        Assert.False(outgoing.RetryFormAction(2,Now));Assert.False(outgoing.DeliveryReviewWasSent);
        var manual=WhatsAppOutgoingMessage.Create(Guid.NewGuid(),Guid.NewGuid(),Guid.NewGuid(),"Manual",Now);manual.Start(Now);manual.MarkUncertain();
        Assert.False(manual.ReviewFormAction(1,false,Guid.NewGuid(),Now));manual.Review();
        Assert.Null(manual.DeliveryReviewWasSent);
        var failed=WhatsAppOutgoingMessage.Create(Guid.NewGuid(),Guid.NewGuid(),Guid.NewGuid(),"Manual",Now);failed.Fail();
        Assert.Throws<InvalidOperationException>(()=>failed.Accept("wamid.invalid-late"));
    }
    [Fact]
    public void A_review_from_before_retry_cannot_resolve_a_later_uncertain_attempt()
    {
        var outgoing=Create();var actor=Guid.NewGuid();var first=outgoing.ReserveFormAction(Now);outgoing.MarkFormActionMessageEffect(first,Now);outgoing.MarkUncertain();
        Assert.True(outgoing.ReviewFormAction(1,false,actor,Now));Assert.True(outgoing.RetryFormAction(2,Now));
        var retry=outgoing.ReserveFormAction(Now);outgoing.MarkFormActionMessageEffect(retry,Now);outgoing.MarkUncertain();
        Assert.False(outgoing.ReviewFormAction(1,false,actor,Now));Assert.Equal(WhatsAppOutgoingMessageState.Uncertain,outgoing.State);
    }
    [Fact]
    public void Replaying_an_old_retry_cannot_reopen_another_failure()
    {
        var outgoing=Create();outgoing.Fail("rejected");Assert.True(outgoing.RetryFormAction(2,Now));
        var retry=outgoing.ReserveFormAction(Now);outgoing.MarkFormActionMessageEffect(retry,Now);outgoing.Fail("rejected-again");
        Assert.False(outgoing.RetryFormAction(2,Now));Assert.Equal(WhatsAppOutgoingMessageState.Failed,outgoing.State);
    }
    [Fact]
    public void Retry_only_reopens_a_confirmed_failure_and_never_an_accepted_message()
    {
        var outgoing = Create(); outgoing.Fail("WindowClosed");
        Assert.True(outgoing.RetryFormAction(2,Now));
        var reservation = outgoing.ReserveFormAction(Now);
        Assert.True(outgoing.MarkFormActionMessageEffect(reservation, Now));
        outgoing.Accept("wamid.confirmed");
        Assert.False(outgoing.RetryFormAction(2,Now));
        Assert.Equal("wamid.confirmed", outgoing.WaMessageId);
        Assert.Null(outgoing.FailureCode);
    }
}
