using ArquitecturaBase.Application.Modules.Arca.Models.Invoicing;
using ArquitecturaBase.Domain.Modules.Arca;
using ArquitecturaBase.Domain.Modules.Arca.Invoicing;
using ArquitecturaBase.Domain.Results;
using ArquitecturaBase.Domain.ValueObjects;

namespace ArquitecturaBase.Application.Modules.Arca.Services.Invoicing;

/// <summary>
/// Antes de encolar, el servidor valida lo que pide el comprobante y recalcula los renglones con half-even (ADR 0028): el
/// documento del receptor (CUIT con su dígito, DNI, o consumidor final sin identificar), el concepto, el período y el
/// vencimiento del pago de un servicio, la fecha que ARCA acepta hoy y los renglones, con el IVA de cada uno si lo trae. Con
/// la clase decidida, <see cref="ForClass"/> pide CUIT en la A y el IVA de cada renglón en la A y la B, y calcula los importes.
/// Un total distinto del que calculó quien pide no se encola.
/// </summary>
internal static class ArcaVoucherOrderPolicy
{
    public const int UnidentifiedDocument = 99;
    private const int CuitDocument = 80;
    private const int CuilDocument = 86;
    private const int CdiDocument = 87;
    private const int DniDocument = 96;
    private const int MaxTextLength = 200;
    private const int MaxItems = 100;

    public static Result<IReadOnlyList<ArcaVoucherLine>> Lines(ArcaVoucherOrder order, DateOnly today)
    {
        if (!ReceiverIsValid(order.Receiver)) return ArcaInvoicingErrors.ReceiverInvalid;
        if (order.Concept is < 1 or > 3) return ArcaInvoicingErrors.ConceptInvalid;
        if (!PeriodIsValid(order)) return ArcaInvoicingErrors.ServicePeriodInvalid;
        if (!ArcaIssueDateWindow.Admits(order.Concept, order.IssueOn, today)) return ArcaInvoicingErrors.IssueDateOutOfRange;
        if (order.Items.Count is 0 or > MaxItems || order.Items.Any(item => !ItemIsValid(item))) return ArcaInvoicingErrors.ItemsInvalid;

        IReadOnlyList<ArcaVoucherLine> lines = [.. order.Items.Select(item => new ArcaVoucherLine(item.Description.Trim(), item.Quantity, item.UnitPrice,
            ArcaInvoiceLine.Amount(item.Quantity, item.UnitPrice), item.Vat))];
        if (lines.Sum(line => line.Amount) <= 0m) return ArcaInvoicingErrors.ItemsInvalid;
        return Result.Success(lines);
    }

    /// <summary>
    /// Los renglones que se fijan y los importes de la clase decidida. La A va con CUIT (10013); la A y la B, con el IVA de cada
    /// renglón; la C no discrimina IVA y lo descarta.
    /// </summary>
    public static Result<(IReadOnlyList<ArcaVoucherLine> Lines, ArcaVoucherAmounts Amounts)> ForClass(ArcaVoucherClass voucherClass,
        ArcaVoucherOrder order, IReadOnlyList<ArcaVoucherLine> lines)
    {
        if (voucherClass == ArcaVoucherClass.A && order.Receiver.DocumentType != CuitDocument) return ArcaInvoicingErrors.ReceiverInvalid;
        if (voucherClass == ArcaVoucherClass.C) lines = [.. lines.Select(line => line with { Vat = null })];
        else if (lines.Any(line => line.Vat is null)) return ArcaInvoicingErrors.ItemsInvalid;

        var amounts = ArcaVoucherRequestPolicy.Amounts(voucherClass, lines, order.PricesIncludeVat);
        if (order.ExpectedTotal is { } expected && expected != amounts.Total) return ArcaInvoicingErrors.TotalMismatch;
        return (lines, amounts);
    }

    private static bool ReceiverIsValid(ArcaVoucherParty receiver)
    {
        if (string.IsNullOrWhiteSpace(receiver.Name) || receiver.Name.Trim().Length > MaxTextLength || receiver.Address?.Length > MaxTextLength) return false;
        return receiver.DocumentType switch
        {
            CuitDocument or CuilDocument => CuitNumber.Normalize(receiver.DocumentNumber.ToString("D11", System.Globalization.CultureInfo.InvariantCulture)) is not null,
            CdiDocument => receiver.DocumentNumber is > 0 and <= 99_999_999_999,
            DniDocument => receiver.DocumentNumber is > 0 and <= 99_999_999,
            UnidentifiedDocument => receiver.DocumentNumber == 0 && receiver.VatConditionId == ArcaReceiverVatConditions.FinalConsumer,
            _ => false,
        };
    }

    private static bool PeriodIsValid(ArcaVoucherOrder order) => order.Concept == 1
        ? order.ServiceFrom is null && order.ServiceTo is null && order.PaymentDueOn is null
        : order is { ServiceFrom: { } from, ServiceTo: { } to, PaymentDueOn: { } due } && to >= from && due >= order.IssueOn;

    private static bool ItemIsValid(ArcaVoucherOrderItem item) =>
        !string.IsNullOrWhiteSpace(item.Description) && item.Description.Trim().Length <= MaxTextLength && item.Quantity > 0m && item.UnitPrice >= 0m
        && (item.Vat is null || item.Vat.ToTreatment() is not null);
}
