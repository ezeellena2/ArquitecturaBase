using System.Globalization;
using ArquitecturaBase.Application.Modules.Arca.Models.Invoicing;
using ArquitecturaBase.Domain.Modules.Arca.Connection;
using ArquitecturaBase.Domain.Modules.Arca.Invoicing;

namespace ArquitecturaBase.Application.Modules.Arca.Services.Invoicing;

/// <summary>
/// Lo que muestra el PDF fiscal de un comprobante autorizado, en palabras y en español: el diseño es fijo y legal, separado
/// del diseño libre de los formularios. Los textos de las condiciones frente al IVA son los de ARCA. La A discrimina el IVA:
/// precios sin IVA con su alícuota y, al pie, el neto, el IVA de cada alícuota y lo exento o no gravado; a un monotributista
/// lleva la leyenda de la RG 5003 (art. 20). La B muestra los precios con IVA y, a un exento, un no alcanzado o un consumidor
/// final, el recuadro de la RG 5614 con el IVA contenido (los otros impuestos indirectos van en 0: no hay tributos).
/// </summary>
internal static class ArcaFiscalDocumentPolicy
{
    public const string MonotaxLegend = "El crédito fiscal discriminado en el presente comprobante, sólo podrá ser computado a efectos del Régimen de "
        + "Sostenimiento e Inclusión Fiscal para Pequeños Contribuyentes de la Ley Nº 27.618";

    private static readonly CultureInfo Spanish = CultureInfo.GetCultureInfo("es-AR");
    private static readonly int[] MonotaxReceivers = [6, 13, 16];
    private static readonly int[] TransparencyReceivers = [4, 5, 15];

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
        var amounts = ArcaVoucherRequestPolicy.Amounts(voucher.Class, payload.Lines, payload.PricesIncludeVat);
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
            [.. payload.Lines.Select(line => Line(voucher.Class, line, payload.PricesIncludeVat))],
            voucher.Total,
            cae,
            due,
            ArcaFiscalQrPolicy.Url(issuedOn, voucher.Cuit, voucher.PointOfSale, voucher.VoucherType, number, voucher.Total, ArcaVoucherRequestPolicy.Pesos, 1m,
                receiver.DocumentType, receiver.DocumentNumber, cae),
            payload.Cancels is { } cancels
                ? $"Anula la Factura {voucher.Class} {FormatNumber(cancels.PointOfSale, cancels.Number)}" +
                  (cancels.IssuedOn is { } date ? $" del {date.ToString("dd/MM/yyyy", CultureInfo.InvariantCulture)}" : string.Empty)
                : null,
            voucher.Class == ArcaVoucherClass.A ? Subtotals(amounts) : [],
            voucher.Class == ArcaVoucherClass.B && TransparencyReceivers.Contains(receiver.VatConditionId) ? amounts.Vat : null,
            voucher.Class == ArcaVoucherClass.A && MonotaxReceivers.Contains(receiver.VatConditionId) ? MonotaxLegend : null);
    }

    /// <summary>La A, sin IVA y con su alícuota; la B, con el IVA incluido; la C, como se cargó. Cada importe, con half-even.</summary>
    private static ArcaFiscalLine Line(ArcaVoucherClass voucherClass, ArcaVoucherLine line, bool pricesIncludeVat)
    {
        var factor = 1m + (line.Vat?.ToTreatment()?.Rate?.Percent ?? 0m) / 100m;
        return voucherClass switch
        {
            ArcaVoucherClass.A when pricesIncludeVat =>
                new(line.Description, line.Quantity, Round(line.UnitPrice / factor), Round(line.Amount / factor), VatLabel(line.Vat)),
            ArcaVoucherClass.A => new(line.Description, line.Quantity, line.UnitPrice, line.Amount, VatLabel(line.Vat)),
            ArcaVoucherClass.B when !pricesIncludeVat => new(line.Description, line.Quantity, Round(line.UnitPrice * factor), Round(line.Amount * factor)),
            _ => new(line.Description, line.Quantity, line.UnitPrice, line.Amount),
        };
    }

    private static List<ArcaFiscalAmount> Subtotals(ArcaVoucherAmounts amounts)
    {
        List<ArcaFiscalAmount> subtotals = [new("Importe neto gravado", amounts.Net)];
        subtotals.AddRange(amounts.VatRates.Select(rate => new ArcaFiscalAmount($"IVA {Percent(rate.RateId)}", rate.Amount)));
        if (amounts.Exempt > 0m) subtotals.Add(new("Importe exento", amounts.Exempt));
        if (amounts.NotTaxed > 0m) subtotals.Add(new("Importe no gravado", amounts.NotTaxed));
        return subtotals;
    }

    private static string? VatLabel(ArcaVoucherVat? vat) => vat switch
    {
        { Kind: ArcaVatTreatmentKind.Taxed, RateId: { } id } => Percent(id),
        { Kind: ArcaVatTreatmentKind.Exempt } => "Exento",
        { Kind: ArcaVatTreatmentKind.NotTaxed } => "No gravado",
        _ => null,
    };

    private static string Percent(int rateId) =>
        (ArcaVatRates.Find(rateId) ?? throw new InvalidOperationException("An ARCA voucher has an unknown VAT rate.")).Percent.ToString("0.##", Spanish) + " %";

    private static decimal Round(decimal value) => Math.Round(value, 2, MidpointRounding.ToEven);

    private static string TaxStatus(ArcaTaxStatus status) => status switch
    {
        ArcaTaxStatus.Monotax => "Responsable Monotributo",
        ArcaTaxStatus.Exempt => "IVA Sujeto Exento",
        ArcaTaxStatus.RegisteredTaxpayer => "IVA Responsable Inscripto",
        _ => "No inscripto",
    };

    private static string FormatCuit(string cuit) => cuit.Length == 11 ? $"{cuit[..2]}-{cuit[2..10]}-{cuit[10]}" : cuit;
}
