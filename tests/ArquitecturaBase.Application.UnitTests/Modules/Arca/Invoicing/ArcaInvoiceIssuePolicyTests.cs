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
        var issued = ArcaInvoiceIssuePolicy.Response(Issued(ArcaVoucherKind.Invoice, new ArcaVoucherParty(80, 30500010912, "Lácteos del Oeste SRL", 1, "Ruta 5 km 30")));

        Assert.Equal(StructuredIssueState.Issued, issued.State);
        Assert.Equal("arca.invoice", issued.Result!.Source);
        Assert.Equal(
            [
                ("voucher", "Factura C"), ("number", "00004-00000012"), ("issuedOn", "2026-11-09"), ("cae", "86123456789012"), ("caeDueOn", "2026-11-19"),
                ("customer", "Lácteos del Oeste SRL"), ("customerDocument", "CUIT 30-50001091-2"), ("customerVatCondition", "IVA Responsable Inscripto"),
                ("total", "3101.5"),
            ],
            issued.Result.Values.Select(value => (value.Key, value.Value)));
        Assert.Equal(("Vence el CAE", "CAE due date"), (Label(issued, "caeDueOn", "es"), Label(issued, "caeDueOn", "en")));
        Assert.Equal(("arca-vouchers/x.pdf", "Factura C 00004-00000012.pdf", "application/pdf", 2048L, "abc"),
            (issued.Document!.StorageKey, issued.Document.Name, issued.Document.ContentType, issued.Document.Size, issued.Document.Sha256));
    }

    // The zone «Comprobante» and the PDF say the same of the receiver: a final consumer without a document is «Sin identificar».
    [Fact]
    public void An_issued_credit_note_to_a_final_consumer_says_its_customer_is_not_identified()
    {
        var issued = ArcaInvoiceIssuePolicy.Response(Issued(ArcaVoucherKind.CreditNote, new ArcaVoucherParty(99, 0, "Consumidor final", 5, null)));

        var values = issued.Result!.Values.ToDictionary(value => value.Key, value => value.Value);
        Assert.Equal(("Nota de crédito C", "2026-11-09", "Consumidor final", "Sin identificar", "Consumidor Final"),
            (values["voucher"], values["issuedOn"], values["customer"], values["customerDocument"], values["customerVatCondition"]));
    }

    // The copy goes to the email of the customer only when «Mandar copia al cliente» was marked and the customer has a valid one.
    [Fact]
    public void The_copy_goes_to_the_email_of_the_customer_only_when_it_was_asked_for_and_the_customer_has_one()
    {
        static StructuredIssueDatum[] Data(string? sendCopy, string? email) =>
        [
            new("customer", "c-1", "Juana Pérez", email is null ? null : new Dictionary<string, string> { ["email"] = email, ["tax_id"] = "20123456786" }),
            .. sendCopy is null ? Array.Empty<StructuredIssueDatum>() : [new StructuredIssueDatum("sendCopy", sendCopy)],
        ];

        Assert.Equal("juana@example.com", ArcaInvoiceIssuePolicy.CopyRecipient(Data("true", " Juana <juana@example.com> ")));
        Assert.Null(ArcaInvoiceIssuePolicy.CopyRecipient(Data("false", "juana@example.com")));
        Assert.Null(ArcaInvoiceIssuePolicy.CopyRecipient(Data(null, "juana@example.com")));
        Assert.Null(ArcaInvoiceIssuePolicy.CopyRecipient(Data("true", null)));
        Assert.Null(ArcaInvoiceIssuePolicy.CopyRecipient(Data("true", "juana")));
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
        Assert.Equal(["voucher", "number", "issuedOn", "cae", "caeDueOn", "customer", "customerDocument", "customerVatCondition", "total"],
            ArcaInvoiceIssuePolicy.ResultFields.Select(field => field.Key));
        Assert.Equal("Emitir comprobante en ARCA", ArcaInvoiceIssuePolicy.Name["es"]);
        Assert.Equal(
            [
                StructuredRecordResultField.Text, StructuredRecordResultField.Text, StructuredRecordResultField.Date, StructuredRecordResultField.Text,
                StructuredRecordResultField.Date, StructuredRecordResultField.Text, StructuredRecordResultField.Text, StructuredRecordResultField.Text,
                StructuredRecordResultField.Number,
            ],
            ArcaInvoiceIssuePolicy.ResultFields.Select(field => field.Kind));
        Assert.Equal(
            [
                ("Fecha", "Date"), ("Vence el CAE", "CAE due date"), ("Cliente", "Customer"), ("Documento", "Document"),
                ("Condición frente al IVA", "VAT condition"),
            ],
            ArcaInvoiceIssuePolicy.ResultFields.Where(field => field.Key is "issuedOn" or "caeDueOn" or "customer" or "customerDocument" or "customerVatCondition")
                .Select(field => (field.Label["es"], field.Label["en"])));
    }

    private static string Label(StructuredIssueResponse issued, string key, string culture) =>
        issued.Result!.Values.Single(value => value.Key == key).Label[culture];

    private static ArcaVoucherResponse Issued(ArcaVoucherKind kind, ArcaVoucherParty receiver) => Voucher(ArcaVoucherState.Authorized) with
    {
        Kind = kind, Receiver = receiver, PdfStorageKey = "arca-vouchers/x.pdf", PdfSha256 = "abc", PdfSize = 2048,
    };

    private static ArcaVoucherResponse Voucher(ArcaVoucherState state) => new(Guid.NewGuid(), Guid.NewGuid(), state, null, ArcaVoucherClass.C,
        ArcaVoucherKind.Invoice, 11, 4, 12, "86123456789012", new DateOnly(2026, 11, 19), new DateOnly(2026, 11, 9), new DateOnly(2026, 11, 9), 3101.5m,
        null, false, Next, 1, null, null, null);
}
