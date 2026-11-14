using ArquitecturaBase.Domain.Modules.WhatsApp;

namespace ArquitecturaBase.Domain.UnitTests.Modules.WhatsApp;

public sealed class WhatsAppOperatorChatTests
{
    [Theory]
    [InlineData("\0", false)]
    [InlineData("\u200B", false)]
    [InlineData(" \n\t", false)]
    [InlineData("line one\nline two\tvisible", true)]
    [InlineData("👨‍👩‍👧‍👦", true)]
    [InlineData("\u0301", false)]
    public void Flow_message_body_rejects_unstorable_or_invisible_text_but_preserves_layout(string text, bool expected) =>
        Assert.Equal(expected, WhatsAppOutgoingMessage.IsVisibleBody(text));

    private static readonly DateTime Now = new(2026, 10, 2, 12, 0, 0, DateTimeKind.Utc);

    [Fact]
    public void Switching_mode_keeps_contact_identity_and_advances_revision_only_on_change()
    {
        var contact = WhatsAppContact.Create("5493413654813", null, "Ana", Now);
        Assert.Equal(WhatsAppConversationMode.Manual, contact.Mode);
        contact.ChangeMode(WhatsAppConversationMode.Assistant);
        Assert.Equal(1, contact.ModeRevision);
        contact.ChangeMode(WhatsAppConversationMode.Assistant);
        Assert.Equal(1, contact.ModeRevision);
        Assert.Equal("5493413654813", contact.WaId);
    }

    [Fact]
    public void Read_position_never_moves_backwards_and_belongs_to_one_operator()
    {
        var state = WhatsAppReadState.Create(Guid.NewGuid(), Guid.NewGuid());
        state.Advance(5);
        state.Advance(2);
        Assert.Equal(5, state.LastReadSequence);
        Assert.Throws<ArgumentOutOfRangeException>(() => state.Advance(-1));
    }

    [Fact]
    public void A_reserved_send_is_not_repeated_and_expiration_is_uncertain()
    {
        var operation = WhatsAppOutgoingMessage.Create(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), "Hola", Now);
        Assert.Equal(WhatsAppOutgoingMessageState.Pending, operation.State);
        operation.Start(Now);
        Assert.Throws<InvalidOperationException>(() => operation.Start(Now));
        operation.MarkUncertain();
        Assert.Equal(WhatsAppOutgoingMessageState.Uncertain, operation.State);
        Assert.Throws<InvalidOperationException>(() => operation.Start(Now));
        operation.Accept("wamid.confirmed");
        Assert.Equal(WhatsAppOutgoingMessageState.Accepted, operation.State);
        Assert.Equal("wamid.confirmed", operation.WaMessageId);
    }

    [Fact]
    public void Accepted_sends_cannot_be_changed_to_failure()
    {
        var operation = WhatsAppOutgoingMessage.Create(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), "Hola", Now);
        operation.Start(Now);
        operation.Accept("wamid.confirmed");
        Assert.Throws<InvalidOperationException>(() => operation.Fail());
        Assert.Equal(WhatsAppOutgoingMessageState.Accepted, operation.State);
    }

    // H7: a manual send that did not go out waits for its next attempt in its place; reserving it again clears the wait.
    [Fact]
    public void A_manual_send_that_did_not_go_out_waits_for_its_next_attempt_in_its_place()
    {
        var operation = WhatsAppOutgoingMessage.Create(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), "Hola", Now);
        operation.Start(Now.AddSeconds(1));

        Assert.True(operation.RetryLater(null, Now.AddSeconds(31)));

        Assert.Equal((WhatsAppOutgoingMessageState.Pending, (DateTime?)Now.AddSeconds(31), Now), (operation.State, operation.NextAttemptAtUtc, operation.RequestedAtUtc));
        Assert.False(operation.RetryLater(null, Now.AddSeconds(60)));
        operation.Start(Now.AddSeconds(31));
        Assert.Null(operation.NextAttemptAtUtc);
        Assert.Equal(1, operation.DispatchRetries);
        Assert.True(operation.RetryLater(null, Now.AddSeconds(62)));
        Assert.Equal(2, operation.DispatchRetries);
    }

    // H9: a refusal Meta reported with its own code, also later in a status, keeps the stable code of its kind.
    [Theory]
    [InlineData(131030, "WhatsApp.Send.NotAllowed")]
    [InlineData(131047, "WhatsApp.Send.WindowClosed")]
    [InlineData(131026, "WhatsApp.Send.Undeliverable")]
    [InlineData(130429, "WhatsApp.Send.RateLimited")]
    [InlineData(131056, "WhatsApp.Send.RateLimited")]
    [InlineData(190, "WhatsApp.Send.TokenInvalid")]
    [InlineData(10, "WhatsApp.Send.PermissionMissing")]
    [InlineData(131016, "WhatsApp.Send.Unavailable")]
    [InlineData(131049, "WhatsApp.Send.Rejected")]
    public void A_refusal_reported_by_its_meta_code_keeps_its_stable_code(int metaCode, string code) =>
        Assert.Equal(code, WhatsAppSendErrors.FromMetaCode(metaCode)?.Code);

    [Fact]
    public void Without_a_meta_code_there_is_no_stable_code() => Assert.Null(WhatsAppSendErrors.FromMetaCode(null));

    // H3: a send whose turn could not be taken goes back to wait for its next attempt, without spending one of its retries.
    [Fact]
    public void A_send_whose_turn_could_not_be_taken_waits_without_spending_a_retry()
    {
        var operation = WhatsAppOutgoingMessage.Create(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), "Hola", Now);
        operation.Start(Now);

        Assert.True(operation.ReturnUnsent(null, Now.AddSeconds(30)));

        Assert.Equal((WhatsAppOutgoingMessageState.Pending, (DateTime?)Now.AddSeconds(30), 0), (operation.State, operation.NextAttemptAtUtc, operation.DispatchRetries));
    }

    // H7: a notice that failed after its retries starts a new round when it is retried by hand.
    [Fact]
    public void A_notice_retried_by_hand_starts_its_retries_again()
    {
        var notice = WhatsAppOutgoingMessage.CreateFormAction(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), "Aviso", Now);
        for (var attempt = 0; attempt < 2; attempt++)
        {
            var reservation = notice.ReserveFormAction(Now);
            Assert.True(notice.RetryLater(reservation, Now.AddSeconds(30)));
        }
        notice.ReserveFormAction(Now);
        notice.Fail("WhatsApp.FormAction.SendRejected");

        Assert.True(notice.RetryFormAction(1, Now.AddMinutes(5)));

        Assert.Equal((0, (DateTime?)null, WhatsAppOutgoingMessageState.Pending), (notice.DispatchRetries, notice.NextAttemptAtUtc, notice.State));
    }

    // H8: a manual send that stopped before Meta was called goes back to the queue in its place; anything else does not move.
    [Fact]
    public void A_manual_send_stopped_before_Meta_goes_back_to_the_queue_in_its_place()
    {
        var operation = WhatsAppOutgoingMessage.Create(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), "Hola", Now);
        operation.Start(Now.AddSeconds(1));

        Assert.True(operation.ReturnUnsent(null));

        Assert.Equal((WhatsAppOutgoingMessageState.Pending, (DateTime?)null, Now), (operation.State, operation.StartedAtUtc, operation.RequestedAtUtc));
        Assert.False(operation.ReturnUnsent(null));
        operation.Start(Now.AddSeconds(2));
        operation.Accept("wamid.sent");
        Assert.False(operation.ReturnUnsent(null));
        Assert.Equal(WhatsAppOutgoingMessageState.Accepted, operation.State);
    }

    // H2: any operator who can send reviews an uncertain send, and the row keeps who and when, as the review of a notice does;
    // reviewing does not say it arrived nor sends it again.
    [Fact]
    public void Reviewing_an_uncertain_send_keeps_who_and_when_without_saying_it_arrived()
    {
        var operation = WhatsAppOutgoingMessage.Create(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), "Hola", Now);
        operation.Start(Now);
        operation.MarkUncertain();
        var reviewer = Guid.NewGuid();

        operation.Review(reviewer, Now.AddMinutes(5));

        Assert.Equal((WhatsAppOutgoingMessageState.Reviewed, (Guid?)reviewer, (DateTime?)Now.AddMinutes(5)),
            (operation.State, operation.DeliveryReviewedByUserId, operation.DeliveryReviewedAtUtc));
        Assert.Null(operation.DeliveryReviewWasSent);
        Assert.Null(operation.WaMessageId);
        var other = WhatsAppOutgoingMessage.Create(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), "Hola", Now);
        other.Start(Now);
        other.MarkUncertain();
        Assert.Throws<ArgumentException>(() => other.Review(Guid.Empty, Now));
    }
}
