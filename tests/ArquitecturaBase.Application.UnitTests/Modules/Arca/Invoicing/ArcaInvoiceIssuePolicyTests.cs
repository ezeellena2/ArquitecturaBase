using ArquitecturaBase.Application.Models.StructuredData;
using ArquitecturaBase.Application.Modules.Arca.Channels.Invoicing;
using ArquitecturaBase.Application.Modules.Arca.Models.Invoicing;
using ArquitecturaBase.Domain.Modules.Arca.Invoicing;

namespace ArquitecturaBase.Application.UnitTests.Modules.Arca.Invoicing;

public sealed class ArcaInvoiceIssuePolicyTests
{
    private static readonly DateTime Next = new(2026, 11, 9, 15, 0, 0, DateTimeKind.Utc);

    [Fact]
    public void An_authorized_voucher_with_its_fiscal_pdf_is_issued_with_its_result_and_document()
    {
        var issued = ArcaInvoiceIssuePolicy.Response(Voucher(ArcaVoucherState.Authorized) with { PdfStorageKey = "arca-vouchers/x.pdf", PdfSha256 = "abc", PdfSize = 2048 });

        Assert.Equal(StructuredIssueState.Issued, issued.State);
        Assert.Equal("arca.invoice", issued.Result!.Source);
        Assert.Equal([("voucher", "Factura C"), ("number", "00004-00000012"), ("cae", "86123456789012"), ("caeDueOn", "2026-11-19"), ("total", "3101.5")],
            issued.Result.Values.Select(value => (value.Key, value.Value)));
        Assert.Equal("CAE due date", issued.Result.Values.Single(value => value.Key == "caeDueOn").Label["en"]);
        Assert.Equal(("arca-vouchers/x.pdf", "Factura C 00004-00000012.pdf", "application/pdf", 2048L, "abc"),
            (issued.Document!.StorageKey, issued.Document.Name, issued.Document.ContentType, issued.Document.Size, issued.Document.Sha256));
    }

    [Fact]
    public void An_authorized_voucher_still_waiting_for_its_pdf_is_pending_on_the_queue_schedule()
    {
        var pending = ArcaInvoiceIssuePolicy.Response(Voucher(ArcaVoucherState.Authorized));

        Assert.Equal((StructuredIssueState.Pending, Next), (pending.State, pending.NextCheckAtUtc));
    }

    [Fact]
    public void A_voucher_waiting_for_arca_is_pending_and_says_so()
    {
        var pending = ArcaInvoiceIssuePolicy.Response(Voucher(ArcaVoucherState.Queued) with { WaitedForArca = true, FailureCode = "arca.unavailable" });

        Assert.Equal((StructuredIssueState.Pending, true, Next, "arca.unavailable"), (pending.State, pending.WaitingForProvider, pending.NextCheckAtUtc, pending.FailureCode));
    }

    [Fact]
    public void Review_rejection_and_cancellation_keep_their_reason_and_revision()
    {
        var review = ArcaInvoiceIssuePolicy.Response(Voucher(ArcaVoucherState.NeedsReview) with { ReviewReason = ArcaVoucherReviewReason.DateOutOfRange, Revision = 3 });
        var rejected = ArcaInvoiceIssuePolicy.Response(Voucher(ArcaVoucherState.Rejected) with { FailureCode = "wsfe.10015" });

        Assert.Equal((StructuredIssueState.NeedsReview, "arca.invoice.dateOutOfRange", 3L), (review.State, review.ReviewReason, review.Revision));
        Assert.Equal((StructuredIssueState.Rejected, "wsfe.10015", false), (rejected.State, rejected.FailureCode, rejected.CanRetry));
        Assert.Equal(StructuredIssueState.Cancelled, ArcaInvoiceIssuePolicy.Response(Voucher(ArcaVoucherState.Cancelled)).State);
    }

    [Fact]
    public void The_issuer_declares_the_data_of_its_result_in_both_languages()
    {
        Assert.Equal(["voucher", "number", "cae", "caeDueOn", "total"], ArcaInvoiceIssuePolicy.ResultFields.Select(field => field.Key));
        Assert.Equal("Emitir comprobante en ARCA", ArcaInvoiceIssuePolicy.Name["es"]);
        Assert.Equal(StructuredRecordResultField.Number, ArcaInvoiceIssuePolicy.ResultFields.Single(field => field.Key == "total").Kind);
    }

    private static ArcaVoucherResponse Voucher(ArcaVoucherState state) => new(Guid.NewGuid(), Guid.NewGuid(), state, null, ArcaVoucherClass.C,
        ArcaVoucherKind.Invoice, 11, 4, 12, "86123456789012", new DateOnly(2026, 11, 19), new DateOnly(2026, 11, 9), new DateOnly(2026, 11, 9), 3101.5m,
        null, false, Next, 1, null, null, null);
}
