using System.Globalization;
using ArquitecturaBase.Application.Modules.Arca.Models.Invoicing;
using ArquitecturaBase.Domain.Modules.Arca.Connection;
using ArquitecturaBase.Domain.Modules.Arca.Invoicing;

namespace ArquitecturaBase.Application.Modules.Arca.Services.Invoicing;

/// <summary>
/// Lo que muestra el PDF fiscal de un comprobante autorizado, en palabras y en español: el diseño es fijo y legal, separado
/// del diseño libre de los formularios. Los textos de las condiciones frente al IVA son los de ARCA.
/// </summary>
internal static class ArcaFiscalDocumentPolicy
{
    private static readonly Dictionary<int, string> VatConditions = new()
    {
        [1] = "IVA Responsable Inscripto", [4] = "IVA Sujeto Exento", [5] = "Consumidor Final", [6] = "Responsable Monotributo",
        [7] = "Sujeto No Categorizado", [8] = "Proveedor del Exterior", [9] = "Cliente del Exterior", [10] = "IVA Liberado – Ley N° 19.640",
        [13] = "Monotributista Social", [15] = "IVA No Alcanzado", [16] = "Monotributo Trabajador Independiente Promovido",
    };

    private static readonly Dictionary<int, string> Documents = new() { [80] = "CUIT", [86] = "CUIL", [87] = "CDI", [96] = "DNI" };

    public static string StorageKey(Guid voucherId) => $"arca-vouchers/{voucherId:N}.pdf";

    public static string FormatNumber(int pointOfSale, long number) =>
        $"{pointOfSale.ToString("D5", CultureInfo.InvariantCulture)}-{number.ToString("D8", CultureInfo.InvariantCulture)}";

    public static string Name(ArcaVoucherClass voucherClass, ArcaVoucherKind kind) =>
        $"{(kind == ArcaVoucherKind.CreditNote ? "Nota de crédito" : "Factura")} {voucherClass}";

    public static ArcaFiscalDocument Build(ArcaVoucher voucher, ArcaVoucherPayload payload)
    {
        if (voucher is not { State: ArcaVoucherState.Authorized, Number: { } number, Cae: { } cae, CaeDueOn: { } due, IssuedOn: { } issuedOn })
            throw new InvalidOperationException("Only an authorized ARCA voucher has a fiscal PDF.");
        var receiver = payload.Receiver;
        return new ArcaFiscalDocument(
            payload.Issuer.LegalName,
            payload.Issuer.FiscalAddress,
            TaxStatus(payload.Issuer.TaxStatus),
            FormatCuit(voucher.Cuit),
            payload.Issuer.GrossIncomeNumber,
            payload.Issuer.ActivityStartOn,
            voucher.Class.ToString(),
            "COD. " + voucher.VoucherType.ToString("D2", CultureInfo.InvariantCulture),
            voucher.Kind == ArcaVoucherKind.CreditNote ? "NOTA DE CRÉDITO" : "FACTURA",
            FormatNumber(voucher.PointOfSale, number),
            issuedOn,
            receiver.Name,
            Documents.TryGetValue(receiver.DocumentType, out var document)
                ? $"{document} {(receiver.DocumentType is 80 or 86 ? FormatCuit(receiver.DocumentNumber.ToString("D11", CultureInfo.InvariantCulture)) : receiver.DocumentNumber.ToString(CultureInfo.InvariantCulture))}"
                : "Sin identificar",
            VatConditions.GetValueOrDefault(receiver.VatConditionId, receiver.VatConditionId.ToString(CultureInfo.InvariantCulture)),
            receiver.Address,
            payload.ServiceFrom,
            payload.ServiceTo,
            payload.PaymentDueOn,
            payload.Lines,
            voucher.Total,
            cae,
            due,
            ArcaFiscalQrPolicy.Url(issuedOn, voucher.Cuit, voucher.PointOfSale, voucher.VoucherType, number, voucher.Total, ArcaVoucherRequestPolicy.Pesos, 1m,
                receiver.DocumentType, receiver.DocumentNumber, cae),
            payload.Cancels is { } cancels
                ? $"Anula la Factura {voucher.Class} {FormatNumber(cancels.PointOfSale, cancels.Number)}" +
                  (cancels.IssuedOn is { } date ? $" del {date.ToString("dd/MM/yyyy", CultureInfo.InvariantCulture)}" : string.Empty)
                : null);
    }

    private static string TaxStatus(ArcaTaxStatus status) => status switch
    {
        ArcaTaxStatus.Monotax => "Responsable Monotributo",
        ArcaTaxStatus.Exempt => "IVA Sujeto Exento",
        ArcaTaxStatus.RegisteredTaxpayer => "IVA Responsable Inscripto",
        _ => "No inscripto",
    };

    private static string FormatCuit(string cuit) => cuit.Length == 11 ? $"{cuit[..2]}-{cuit[2..10]}-{cuit[10]}" : cuit;
}
