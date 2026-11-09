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
    public void Only_an_authorized_voucher_has_a_fiscal_pdf()
    {
        var (voucher, payload) = ArcaVoucherRequestPolicyTests.Invoice();

        Assert.Throws<InvalidOperationException>(() => ArcaFiscalDocumentPolicy.Build(voucher, payload));
    }
}
