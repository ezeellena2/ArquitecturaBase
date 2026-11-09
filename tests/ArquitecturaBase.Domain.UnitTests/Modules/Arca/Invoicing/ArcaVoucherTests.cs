using ArquitecturaBase.Domain.Modules.Arca;
using ArquitecturaBase.Domain.Modules.Arca.Connection;
using ArquitecturaBase.Domain.Modules.Arca.Invoicing;

namespace ArquitecturaBase.Domain.UnitTests.Modules.Arca.Invoicing;

public sealed class ArcaVoucherTests
{
    private static readonly DateTime Now = new(2026, 11, 9, 13, 0, 0, DateTimeKind.Utc);
    private static readonly DateOnly RequestedOn = new(2026, 11, 9);

    [Fact]
    public void A_new_voucher_is_queued_due_now_without_a_number()
    {
        var voucher = New();

        Assert.Equal((ArcaVoucherState.Queued, (long?)null, 11, Now, 0), (voucher.State, voucher.Number, voucher.VoucherType, voucher.NextAttemptAtUtc, voucher.Revision));
        Assert.True(voucher.IsDue(Now));
    }

    [Fact]
    public void A_credit_note_of_class_c_has_the_code_13_and_remembers_what_it_cancels()
    {
        var cancelled = Guid.CreateVersion7();
        var voucher = ArcaVoucher.Create(Guid.CreateVersion7(), ArcaEnvironment.Testing, Guid.CreateVersion7(), ArcaVoucherClass.C, ArcaVoucherKind.CreditNote, "27287654338", 4, 1, 80,
            30712345671, RequestedOn, 605500m, "{}", cancelled, Now);

        Assert.Equal((13, cancelled), (voucher.VoucherType, voucher.CancelsVoucherId));
    }

    [Fact]
    public void Assigning_a_number_starts_sending_it_with_its_hash()
    {
        var voucher = New();

        voucher.AssignNumber(12, "hash", Now);

        Assert.Equal((ArcaVoucherState.Sending, (long?)12, "hash", 1), (voucher.State, voucher.Number, voucher.DataHash, voucher.Revision));
        Assert.True(voucher.HoldsNumber);
        Assert.Throws<InvalidOperationException>(() => voucher.AssignNumber(13, "other", Now));
    }

    [Fact]
    public void A_voucher_sending_is_due_only_once_the_lease_it_was_sent_with_ends()
    {
        var voucher = New();

        voucher.AssignNumber(12, "hash", Now.AddMinutes(2));

        Assert.False(voucher.IsDue(Now.AddSeconds(119)));
        Assert.True(voucher.IsDue(Now.AddMinutes(2)));
    }

    [Fact]
    public void An_authorized_voucher_keeps_its_cae_and_is_issued_on_the_requested_date()
    {
        var voucher = Sending();

        voucher.Authorize("76451234567890", new DateOnly(2026, 11, 19), "[]", Now.AddSeconds(2));

        Assert.Equal((ArcaVoucherState.Authorized, "76451234567890", (DateOnly?)new DateOnly(2026, 11, 19), (DateOnly?)RequestedOn, (DateTime?)Now.AddSeconds(2)),
            (voucher.State, voucher.Cae, voucher.CaeDueOn, voucher.IssuedOn, voucher.CompletedAtUtc));
        Assert.False(voucher.HoldsNumber);
        Assert.False(voucher.IsDue(Now.AddDays(1)));
    }

    [Fact]
    public void A_rejected_voucher_frees_its_number()
    {
        var voucher = Sending();

        voucher.Reject("wsfe.10015", "[{\"code\":10015}]", Now);

        Assert.Equal((ArcaVoucherState.Rejected, (long?)null, "wsfe.10015"), (voucher.State, voucher.Number, voucher.FailureCode));
    }

    [Fact]
    public void An_uncertain_voucher_keeps_its_number_and_waits_more_after_each_failure()
    {
        var voucher = Sending();

        voucher.MarkUncertain("wsfe.unavailable", Now);
        Assert.Equal((ArcaVoucherState.Uncertain, (long?)12, Now.AddSeconds(30)), (voucher.State, voucher.Number, voucher.NextAttemptAtUtc));
        Assert.False(voucher.IsDue(Now.AddSeconds(29)));
        Assert.True(voucher.IsDue(Now.AddSeconds(30)));

        voucher.MarkUncertain("wsfe.unavailable", Now);
        Assert.Equal(Now.AddMinutes(1), voucher.NextAttemptAtUtc);
        Assert.True(voucher.WaitedForArca);
    }

    [Fact]
    public void An_uncertain_voucher_that_arca_did_not_issue_is_sent_again_with_the_same_number()
    {
        var voucher = Sending();
        voucher.MarkUncertain("wsfe.unavailable", Now);

        voucher.Resend(Now);

        Assert.Equal((ArcaVoucherState.Sending, (long?)12, "hash"), (voucher.State, voucher.Number, voucher.DataHash));
    }

    [Fact]
    public void A_queued_voucher_postponed_by_the_connection_keeps_waiting_without_a_number()
    {
        var voucher = New();

        voucher.Postpone("Arca.Connection.Unavailable", Now);

        Assert.Equal((ArcaVoucherState.Queued, (long?)null, Now.AddSeconds(30), "Arca.Connection.Unavailable"),
            (voucher.State, voucher.Number, voucher.NextAttemptAtUtc, voucher.FailureCode));
        Assert.True(voucher.WaitedForArca);
    }

    [Fact]
    public void A_number_taken_by_someone_else_goes_to_review_and_is_issued_with_the_next_one()
    {
        var voucher = Sending();
        voucher.RequireReview(ArcaVoucherReviewReason.NumberTaken, Now);
        Assert.Equal((ArcaVoucherState.NeedsReview, ArcaVoucherReviewReason.NumberTaken), (voucher.State, voucher.ReviewReason));
        Assert.False(voucher.IsDue(Now.AddDays(1)));

        var reviewed = voucher.Review(ArcaVoucherReviewDecision.IssueWithNextNumber, voucher.Revision, RequestedOn, Now);

        Assert.True(reviewed.IsSuccess);
        Assert.Equal((ArcaVoucherState.Queued, (long?)null, (string?)null, (ArcaVoucherReviewReason?)null), (voucher.State, voucher.Number, voucher.DataHash, voucher.ReviewReason));
        Assert.True(voucher.IsDue(Now));
    }

    [Fact]
    public void A_date_out_of_range_is_issued_today_after_review()
    {
        var voucher = New();
        voucher.RequireReview(ArcaVoucherReviewReason.DateOutOfRange, Now);

        var reviewed = voucher.Review(ArcaVoucherReviewDecision.IssueToday, voucher.Revision, new DateOnly(2026, 11, 16), Now);

        Assert.True(reviewed.IsSuccess);
        Assert.Equal((ArcaVoucherState.Queued, new DateOnly(2026, 11, 16)), (voucher.State, voucher.RequestedOn));
    }

    [Fact]
    public void Not_issuing_after_review_cancels_the_voucher()
    {
        var voucher = New();
        voucher.RequireReview(ArcaVoucherReviewReason.DateOutOfRange, Now);

        Assert.True(voucher.Review(ArcaVoucherReviewDecision.DoNotIssue, voucher.Revision, RequestedOn, Now).IsSuccess);
        Assert.Equal(ArcaVoucherState.Cancelled, voucher.State);
    }

    [Fact]
    public void A_review_needs_the_voucher_in_review_its_revision_and_a_decision_that_fits_the_reason()
    {
        var voucher = New();
        Assert.Equal(ArcaInvoicingErrors.VoucherNotInReview, voucher.Review(ArcaVoucherReviewDecision.DoNotIssue, 0, RequestedOn, Now).Error);

        voucher.RequireReview(ArcaVoucherReviewReason.DateOutOfRange, Now);
        Assert.Equal(ArcaInvoicingErrors.VoucherRevisionConflict, voucher.Review(ArcaVoucherReviewDecision.DoNotIssue, voucher.Revision - 1, RequestedOn, Now).Error);
        Assert.Equal(ArcaInvoicingErrors.ReviewDecisionInvalid, voucher.Review(ArcaVoucherReviewDecision.IssueWithNextNumber, voucher.Revision, RequestedOn, Now).Error);
    }

    [Fact]
    public void Only_a_voucher_without_a_number_can_be_cancelled()
    {
        var queued = New();
        Assert.True(queued.Cancel(Now).IsSuccess);
        Assert.Equal(ArcaVoucherState.Cancelled, queued.State);

        Assert.Equal(ArcaInvoicingErrors.VoucherNotCancellable, Sending().Cancel(Now).Error);
    }

    [Fact]
    public void An_authorized_voucher_is_due_for_its_pdf_until_it_has_one()
    {
        var voucher = Sending();
        voucher.MarkUncertain("wsfe.unavailable", Now);
        voucher.Authorize("76451234567890", new DateOnly(2026, 11, 19), null, Now);
        Assert.True(voucher.IsPdfDue(Now));

        voucher.PostponePdf("Files.Unavailable", Now);
        Assert.False(voucher.IsPdfDue(Now.AddSeconds(29)));
        Assert.True(voucher.IsPdfDue(Now.AddSeconds(30)));

        voucher.AttachPdf("arca-vouchers/x.pdf", "sha", 1234);
        Assert.Equal(("arca-vouchers/x.pdf", "sha", (long?)1234), (voucher.PdfStorageKey, voucher.PdfSha256, voucher.PdfSize));
        Assert.False(voucher.IsPdfDue(Now.AddDays(1)));
        Assert.Equal(ArcaVoucherState.Authorized, voucher.State);
    }

    [Fact]
    public void A_voucher_that_is_not_authorized_has_no_pdf()
    {
        Assert.False(Sending().IsPdfDue(Now));
        Assert.Throws<InvalidOperationException>(() => Sending().AttachPdf("key", "sha", 1));
    }

    private static ArcaVoucher New() => ArcaVoucher.Create(Guid.CreateVersion7(), ArcaEnvironment.Testing, Guid.CreateVersion7(), ArcaVoucherClass.C, ArcaVoucherKind.Invoice,
        "27287654338", 4, 1, 80, 30712345671, RequestedOn, 605500m, "{}", null, Now);

    private static ArcaVoucher Sending()
    {
        var voucher = New();
        voucher.AssignNumber(12, "hash", Now);
        return voucher;
    }
}
