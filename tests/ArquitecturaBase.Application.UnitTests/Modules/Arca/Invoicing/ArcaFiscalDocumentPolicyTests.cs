using System.Text;
using System.Text.Json;
using ArquitecturaBase.Application.Modules.Arca.Models.Invoicing;
using ArquitecturaBase.Application.Modules.Arca.Services.Invoicing;
using ArquitecturaBase.Domain.Modules.Arca.Connection;
using ArquitecturaBase.Domain.Modules.Arca.Invoicing;

namespace ArquitecturaBase.Application.UnitTests.Modules.Arca.Invoicing;

public sealed class ArcaFiscalDocumentPolicyTests
{
    private static readonly DateTime Now = new(2026, 11, 9, 13, 0, 0, DateTimeKind.Utc);

    [Fact]
    public void The_qr_carries_the_version_1_json_of_arca_in_base64()
    {
        var url = ArcaFiscalQrPolicy.Url(new DateOnly(2026, 11, 9), "27287654338", 4, 11, 12, 605500m, "PES", 1m, 80, 30712345671, "76451234567890");

        Assert.StartsWith("https://www.arca.gob.ar/fe/qr/?p=", url, StringComparison.Ordinal);
        using var json = JsonDocument.Parse(Encoding.UTF8.GetString(Convert.FromBase64String(url["https://www.arca.gob.ar/fe/qr/?p=".Length..])));
        var root = json.RootElement;
        Assert.Equal((1, "2026-11-09", 27287654338L, 4, 11, 12L, 605500m, "PES", 1m, 80, 30712345671L, "E", 76451234567890L),
            (root.GetProperty("ver").GetInt32(), root.GetProperty("fecha").GetString(), root.GetProperty("cuit").GetInt64(), root.GetProperty("ptoVta").GetInt32(),
             root.GetProperty("tipoCmp").GetInt32(), root.GetProperty("nroCmp").GetInt64(), root.GetProperty("importe").GetDecimal(),
             root.GetProperty("moneda").GetString(), root.GetProperty("ctz").GetDecimal(), root.GetProperty("tipoDocRec").GetInt32(),
             root.GetProperty("nroDocRec").GetInt64(), root.GetProperty("tipoCodAut").GetString(), root.GetProperty("codAut").GetInt64()));
    }

    [Fact]
    public void A_final_consumer_without_a_document_goes_without_a_receiver_in_the_qr()
    {
        var url = ArcaFiscalQrPolicy.Url(new DateOnly(2026, 11, 9), "27287654338", 4, 11, 12, 100m, "PES", 1m, 99, 0, "76451234567890");

        using var json = JsonDocument.Parse(Encoding.UTF8.GetString(Convert.FromBase64String(url["https://www.arca.gob.ar/fe/qr/?p=".Length..])));
        Assert.False(json.RootElement.TryGetProperty("tipoDocRec", out _));
        Assert.False(json.RootElement.TryGetProperty("nroDocRec", out _));
    }

    [Fact]
    public void An_authorized_invoice_shows_its_class_code_number_receiver_and_cae()
    {
        var (voucher, payload) = ArcaVoucherRequestPolicyTests.Invoice();
        voucher.AssignNumber(12, "hash", Now);
        voucher.Authorize("76451234567890", new DateOnly(2026, 11, 19), null, Now);

        var document = ArcaFiscalDocumentPolicy.Build(voucher, payload);

        Assert.Equal(("C", "COD. 11", "FACTURA", "00004-00000012", "27-28765433-8", "Responsable Monotributo"),
            (document.ClassLetter, document.TypeCode, document.Title, document.Number, document.IssuerCuit, document.IssuerTaxStatus));
        Assert.Equal(("CUIT 30-71234567-1", "IVA Responsable Inscripto", "76451234567890", 605500m, (string?)null),
            (document.ReceiverDocument, document.ReceiverVatCondition, document.Cae, document.Total, document.Cancels));
        Assert.Equal(("27-28765433-8", (DateOnly?)new DateOnly(2019, 3, 1)), (document.GrossIncomeNumber, document.ActivityStartOn));
    }

    // The result of the issuance repeats these texts, so the zone «Comprobante» and the fiscal PDF say the same.
    [Theory]
    [InlineData(80, 30500010912L, "CUIT 30-50001091-2")]
    [InlineData(86, 20123456786L, "CUIL 20-12345678-6")]
    [InlineData(96, 12345678L, "DNI 12345678")]
    [InlineData(99, 0L, "Sin identificar")]
    public void The_document_of_the_receiver_is_its_type_and_number_or_else_not_identified(int type, long number, string expected) =>
        Assert.Equal(expected, ArcaFiscalDocumentPolicy.ReceiverDocument(new ArcaVoucherParty(type, number, "Cliente", 1, null)));

    [Theory]
    [InlineData(1, "IVA Responsable Inscripto")]
    [InlineData(5, "Consumidor Final")]
    [InlineData(6, "Responsable Monotributo")]
    [InlineData(42, "42")]
    public void The_vat_condition_of_the_receiver_is_the_text_of_arca_or_else_its_code(int condition, string expected) =>
        Assert.Equal(expected, ArcaFiscalDocumentPolicy.ReceiverVatCondition(new ArcaVoucherParty(99, 0, "Cliente", condition, null)));

    [Fact]
    public void A_credit_note_says_which_invoice_it_cancels()
    {
        var (_, payload) = ArcaVoucherRequestPolicyTests.Invoice();
        var note = ArcaVoucher.Create(Guid.CreateVersion7(), ArcaEnvironment.Testing, Guid.CreateVersion7(), ArcaVoucherClass.C, ArcaVoucherKind.CreditNote,
            "27287654338", 4, 1, 80, 30712345671, new DateOnly(2026, 11, 10), 605500m, "{}", Guid.CreateVersion7(), Now);
        note.AssignNumber(1, "hash", Now);
        note.Authorize("76451234598765", new DateOnly(2026, 11, 20), null, Now);

        var document = ArcaFiscalDocumentPolicy.Build(note,
            payload with { Cancels = new ArcaAssociatedVoucher(11, 4, 12, "27287654338", new DateOnly(2026, 11, 9)) });

        Assert.Equal(("NOTA DE CRÉDITO", "COD. 13", "Anula la Factura C 00004-00000012 del 09/11/2026"), (document.Title, document.TypeCode, document.Cancels));
    }

    [Fact]
    public void A_class_c_invoice_shows_its_lines_as_they_are_and_no_vat()
    {
        var (voucher, payload) = ArcaVoucherRequestPolicyTests.Invoice();

        var document = ArcaFiscalDocumentPolicy.Build(Authorized(voucher), payload);

        Assert.Equal([new ArcaFiscalLine("Semilla de soja", 10m, 52000m, 520000m), new ArcaFiscalLine("Flete", 1m, 85500m, 85500m)], document.Lines);
        Assert.Empty(document.Subtotals);
        Assert.Equal(((decimal?)null, (string?)null), (document.VatContained, document.Legend));
    }

    [Fact]
    public void A_class_a_invoice_shows_net_prices_with_their_rate_and_the_vat_of_each_rate()
    {
        var (voucher, payload) = ArcaVoucherRequestPolicyTests.Invoice(ArcaVoucherClass.A, pricesIncludeVat: false);

        var document = ArcaFiscalDocumentPolicy.Build(Authorized(voucher), payload);

        Assert.Equal(("A", "COD. 01", 629200.21m), (document.ClassLetter, document.TypeCode, document.Total));
        Assert.Equal([new ArcaFiscalLine("Semilla de soja", 10m, 52000m, 520000m, "21%"), new ArcaFiscalLine("Flete", 1.5m, 0.125m, 0.19m, "10,5%")],
            document.Lines);
        Assert.Equal([new ArcaFiscalAmount("Importe neto gravado", 520000.19m), new ArcaFiscalAmount("IVA 10,5%", 0.02m), new ArcaFiscalAmount("IVA 21%", 109200m)],
            document.Subtotals);
        Assert.Equal(((decimal?)null, (string?)null), (document.VatContained, document.Legend));
    }

    [Fact]
    public void A_class_a_invoice_with_prices_with_vat_shows_them_without_it()
    {
        var (voucher, payload) = ArcaVoucherRequestPolicyTests.Invoice(ArcaVoucherClass.A, pricesIncludeVat: true);

        var document = ArcaFiscalDocumentPolicy.Build(Authorized(voucher), payload);

        Assert.Equal([new ArcaFiscalLine("Semilla de soja", 10m, 42975.21m, 429752.07m, "21%"), new ArcaFiscalLine("Flete", 1.5m, 0.11m, 0.17m, "10,5%")],
            document.Lines);
        Assert.Equal([new ArcaFiscalAmount("Importe neto gravado", 429752.24m), new ArcaFiscalAmount("IVA 10,5%", 0.02m), new ArcaFiscalAmount("IVA 21%", 90247.93m)],
            document.Subtotals);
        Assert.Equal(520000.19m, document.Total);
    }

    [Fact]
    public void A_class_a_invoice_shows_exempt_and_not_taxed_lines_and_their_subtotals()
    {
        var (voucher, payload) = ArcaVoucherRequestPolicyTests.Invoice(ArcaVoucherClass.A, pricesIncludeVat: false);
        payload = payload with
        {
            Lines =
            [
                payload.Lines[0],
                new ArcaVoucherLine("Libro", 1m, 15000m, 15000m, new(ArcaVatTreatmentKind.Exempt)),
                new ArcaVoucherLine("Sellado", 1m, 500m, 500m, new(ArcaVatTreatmentKind.NotTaxed)),
            ],
        };

        var document = ArcaFiscalDocumentPolicy.Build(Authorized(voucher), payload);

        Assert.Equal(["21%", "Exento", "No gravado"], document.Lines.Select(line => line.Vat));
        Assert.Equal(
            [
                new ArcaFiscalAmount("Importe neto gravado", 520000m), new ArcaFiscalAmount("IVA 21%", 109200m),
                new ArcaFiscalAmount("Importe exento", 15000m), new ArcaFiscalAmount("Importe no gravado", 500m),
            ],
            document.Subtotals);
    }

    [Theory]
    [InlineData(6)]
    [InlineData(13)]
    [InlineData(16)]
    public void A_class_a_invoice_to_a_monotax_receiver_carries_the_legend_of_rg_5003(int condition)
    {
        var (voucher, payload) = ArcaVoucherRequestPolicyTests.Invoice(ArcaVoucherClass.A, pricesIncludeVat: false);

        var document = ArcaFiscalDocumentPolicy.Build(Authorized(voucher), payload with { Receiver = payload.Receiver with { VatConditionId = condition } });

        Assert.Equal("El crédito fiscal discriminado en el presente comprobante, sólo podrá ser computado a efectos del Régimen de Sostenimiento e "
            + "Inclusión Fiscal para Pequeños Contribuyentes de la Ley Nº 27.618", document.Legend);
    }

    [Fact]
    public void A_class_b_invoice_to_a_final_consumer_shows_prices_with_vat_and_the_vat_it_contains()
    {
        var (voucher, payload) = ArcaVoucherRequestPolicyTests.Invoice(ArcaVoucherClass.B, pricesIncludeVat: false);

        var document = ArcaFiscalDocumentPolicy.Build(Authorized(voucher), payload);

        Assert.Equal(("B", "COD. 06", 629200.21m), (document.ClassLetter, document.TypeCode, document.Total));
        Assert.Equal([new ArcaFiscalLine("Semilla de soja", 10m, 62920m, 629200m), new ArcaFiscalLine("Flete", 1.5m, 0.14m, 0.21m)], document.Lines);
        Assert.Empty(document.Subtotals);
        Assert.Equal(((decimal?)109200.02m, (string?)null), (document.VatContained, document.Legend));
    }

    [Theory]
    [InlineData(4, true)]
    [InlineData(5, true)]
    [InlineData(15, true)]
    [InlineData(7, false)]
    [InlineData(10, false)]
    public void Only_a_class_b_invoice_to_an_exempt_a_not_subject_or_a_final_consumer_has_the_transparency_box(int condition, bool shown)
    {
        var (voucher, payload) = ArcaVoucherRequestPolicyTests.Invoice(ArcaVoucherClass.B, pricesIncludeVat: true);

        var document = ArcaFiscalDocumentPolicy.Build(Authorized(voucher), payload with { Receiver = payload.Receiver with { VatConditionId = condition } });

        Assert.Equal(shown ? 90247.95m : null, document.VatContained);
    }

    [Fact]
    public void Only_an_authorized_voucher_has_a_fiscal_pdf()
    {
        var (voucher, payload) = ArcaVoucherRequestPolicyTests.Invoice();

        Assert.Throws<InvalidOperationException>(() => ArcaFiscalDocumentPolicy.Build(voucher, payload));
    }

    private static ArcaVoucher Authorized(ArcaVoucher voucher)
    {
        voucher.AssignNumber(7, "hash", Now);
        voucher.Authorize("76451234567890", new DateOnly(2026, 11, 19), null, Now);
        return voucher;
    }
}
