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
        Assert.Equal("ARCA no responde. Tu factura sale apenas vuelva.", pending.WaitingNotice!["es"]);
        Assert.Null(ArcaInvoiceIssuePolicy.Response(Voucher(ArcaVoucherState.Queued)).WaitingNotice);
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
    public void The_preview_of_an_invoice_c_says_why_and_itemizes_nothing()
    {
        var preview = ArcaInvoiceIssuePolicy.Preview(new ArcaVoucherPreviewResponse(ArcaVoucherClass.C, 11, 3101.5m, 0m)
            { Amounts = new(3101.5m, 0m, 3101.5m, 0m, 0m, 0m, []) }, Order(new(99, 0, "Juana Pérez", 5, null)));

        Assert.Equal(("Factura C", "Invoice C", "Juana Pérez", "3101.5"), (preview.Document!["es"], preview.Document["en"], preview.Receiver, preview.Total));
        Assert.Equal(("Lo decide el sistema: sos monotributista.", "2026-11-09"), (preview.Info!["es"], preview.Date));
        Assert.Null(preview.Amounts);
    }

    [Fact]
    public void The_preview_of_an_invoice_a_itemizes_the_net_each_vat_rate_and_the_exempt_amount()
    {
        var amounts = new ArcaVoucherAmounts(724677.5m, 0m, 605500m, 1000m, 0m, 118177.5m, [new(5, 520000m, 109200m), new(4, 85500m, 8977.5m)]);
        var preview = ArcaInvoiceIssuePolicy.Preview(new ArcaVoucherPreviewResponse(ArcaVoucherClass.A, 1, 724677.5m, 118177.5m) { Amounts = amounts },
            Order(new(80, 30500010912, "Agro SA", 1, null)));

        Assert.Equal([("Importe neto gravado", "605500"), ("IVA 21 %", "109200"), ("IVA 10,5 %", "8977.5"), ("Importe exento", "1000")],
            preview.Amounts!.Select(amount => (amount.Label["es"], amount.Value)));
        Assert.Equal("VAT 10.5%", preview.Amounts![2].Label["en"]);
        Assert.Equal("Lo decide el sistema: sos responsable inscripto y tu cliente también.", preview.Info!["es"]);
    }

    [Fact]
    public void The_preview_of_an_invoice_b_shows_the_vat_it_contains_only_to_a_final_consumer()
    {
        var amounts = new ArcaVoucherAmounts(1210m, 0m, 1000m, 0m, 0m, 210m, [new(5, 1000m, 210m)]);
        var final = ArcaInvoiceIssuePolicy.Preview(new ArcaVoucherPreviewResponse(ArcaVoucherClass.B, 6, 1210m, 210m) { Amounts = amounts }, Order(new(99, 0, "Juana", 5, null)));
        var other = ArcaInvoiceIssuePolicy.Preview(new ArcaVoucherPreviewResponse(ArcaVoucherClass.B, 6, 1210m, 210m) { Amounts = amounts }, Order(new(80, 20123456786, "Otro", 7, null)));

        Assert.Equal(("IVA contenido", "210"), (Assert.Single(final.Amounts!).Label["es"], final.Amounts![0].Value));
        Assert.Null(other.Amounts);
        Assert.Equal("Lo decide el sistema: es consumidor final.", final.Info!["es"]);
    }

    private static ArcaVoucherOrder Order(ArcaVoucherParty receiver) =>
        new(Guid.Empty, receiver, 1, new DateOnly(2026, 11, 9), null, null, null, [], null);

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
