using System.Globalization;
using System.Resources;
using ArquitecturaBase.Application.Models.StructuredData;
using ArquitecturaBase.Application.Modules.Arca.Models.Invoicing;
using ArquitecturaBase.Application.Modules.Arca.Services.Invoicing;
using ArquitecturaBase.Domain.Modules.Arca.Invoicing;

namespace ArquitecturaBase.Application.Modules.Arca.Channels.Invoicing;

/// <summary>
/// An issuance of the queue as forms sees it. It is issued once ARCA authorized it and its fiscal PDF is stored, with the voucher,
/// its number, its date, the CAE and its due date, the customer with its document and VAT condition (as the fiscal PDF says them)
/// and the total as its result; until then it is pending, on the queue's own schedule, with the attempts that failed while it
/// waits for ARCA. A rejection brings the first code and text of ARCA as it sent them («10015 · …»).
/// </summary>
internal static class ArcaInvoiceIssuePolicy
{
    private static readonly ResourceManager Texts = new("ArquitecturaBase.Application.Modules.Arca.Resources.Invoicing", typeof(ArcaInvoiceIssuePolicy).Assembly);
    private static readonly string[] Cultures = ["es", "en"];
    private static readonly string[] ResultKeys =
        ["voucher", "number", "issuedOn", "cae", "caeDueOn", "customer", "customerDocument", "customerVatCondition", "total"];

    public static IReadOnlyDictionary<string, string> Name { get; } = Localized("Issuer.Name");

    public static IReadOnlyList<StructuredRecordResultField> ResultFields { get; } = ResultKeys.Select(key => new StructuredRecordResultField(key,
        Localized("Result." + key), key switch
        {
            "issuedOn" or "caeDueOn" => StructuredRecordResultField.Date,
            "total" => StructuredRecordResultField.Number,
            _ => StructuredRecordResultField.Text,
        })).ToArray();

    public static StructuredIssueResponse Response(ArcaVoucherResponse voucher) => voucher.State switch
    {
        ArcaVoucherState.Authorized when voucher.PdfStorageKey is { } storageKey => new(StructuredIssueState.Issued, Revision: voucher.Revision,
            Result: Result(voucher), Document: new StructuredRecordActionDocument(storageKey,
                $"{ArcaFiscalDocumentPolicy.Name(voucher.Class, voucher.Kind)} {Number(voucher)}.pdf", "application/pdf", voucher.PdfSize!.Value, voucher.PdfSha256!)),
        ArcaVoucherState.Rejected => new(StructuredIssueState.Rejected, voucher.FailureCode, Revision: voucher.Revision)
        {
            FailureDetail = voucher.Messages is [var message, ..] ? $"{message.Code.ToString(CultureInfo.InvariantCulture)} · {message.Text.Trim()}" : null,
        },
        ArcaVoucherState.NeedsReview => new(StructuredIssueState.NeedsReview, Revision: voucher.Revision,
            ReviewReason: ArcaInvoiceStandard.Key + "." + Camel(voucher.ReviewReason!.Value.ToString())),
        ArcaVoucherState.Cancelled => new(StructuredIssueState.Cancelled, Revision: voucher.Revision),
        _ => new(StructuredIssueState.Pending, voucher.FailureCode, WaitingForProvider: voucher.WaitedForArca, NextCheckAtUtc: voucher.NextAttemptAtUtc,
            Revision: voucher.Revision, WaitingNotice: voucher.WaitedForArca
                ? Localized(voucher.Kind == ArcaVoucherKind.CreditNote ? "Notice.WaitingCreditNote" : "Notice.WaitingInvoice") : null)
        {
            // «Ya probamos 3 veces»: the attempts that failed in a row while it waits.
            Attempts = voucher.WaitedForArca && voucher.Failures > 0 ? voucher.Failures : null,
        },
    };

    /// <summary>
    /// The copy to the customer: the email of the customer of «Clientes» when «Mandar copia al cliente» was marked. A final consumer, a
    /// customer written in the invoice by its CUIT or one without a valid email has none, and the copy does not apply.
    /// </summary>
    public static string? CopyRecipient(IReadOnlyList<StructuredIssueDatum> data) =>
        data.Any(datum => datum.Key == ArcaInvoiceStandard.SendCopy && datum.Value == "true")
        && data.FirstOrDefault(datum => datum.Key == ArcaInvoiceStandard.Customer)?.Fields?.GetValueOrDefault(ArcaCustomersCatalog.Email) is { } email
        && System.Net.Mail.MailAddress.TryCreate(email.Trim(), out var address) ? address.Address : null;

    /// <summary>
    /// «¿La emito?»: the voucher the system decided and why, the receiver, the amounts the voucher itemizes (the A, net, VAT by rate,
    /// exempt and not taxed; the B to a final consumer, the VAT it contains) and the recalculated total.
    /// </summary>
    public static StructuredIssuePreview Preview(ArcaVoucherPreviewResponse preview, ArcaVoucherOrder order) =>
        new(Localized("Voucher.Invoice", preview.Class.ToString()), order.Receiver.Name, Canonical(preview.Total))
        {
            Info = Localized(InfoKey(preview, order.Receiver.VatConditionId)),
            Date = order.IssueOn,
            Amounts = preview.Amounts is { } amounts ? Amounts(preview.Class, amounts, order.Receiver.VatConditionId) : null,
        };

    // «Lo decide el sistema: …»: the condition of the company for the C; for the A and the B, the one of the customer.
    private static string InfoKey(ArcaVoucherPreviewResponse preview, int receiverVatConditionId) => preview.Class switch
    {
        ArcaVoucherClass.C => preview.CompanyTaxStatus == ArquitecturaBase.Domain.Modules.Arca.Connection.ArcaTaxStatus.Exempt
            ? "Preview.Info.C.Exempt" : "Preview.Info.C",
        ArcaVoucherClass.A => receiverVatConditionId == 1 ? "Preview.Info.A" : "Preview.Info.A.Monotax",
        _ => receiverVatConditionId == 5 ? "Preview.Info.B" : "Preview.Info.B.Other",
    };

    private static List<StructuredIssueAmount>? Amounts(ArcaVoucherClass voucherClass, ArcaVoucherAmounts amounts, int receiverVatConditionId)
    {
        if (voucherClass == ArcaVoucherClass.B)
            return ArcaFiscalDocumentPolicy.ShowsVatContained(receiverVatConditionId) ? [new(Localized("Preview.VatContained"), Canonical(amounts.Vat))] : null;
        if (voucherClass != ArcaVoucherClass.A) return null;
        List<StructuredIssueAmount> lines = [new(Localized("Preview.Net"), Canonical(amounts.Net))];
        lines.AddRange(amounts.VatRates.Select(rate => new StructuredIssueAmount(
            Localized("Preview.Vat", (ArcaVatRates.Find(rate.RateId) ?? throw new InvalidOperationException("An ARCA voucher has an unknown VAT rate.")).Percent),
            Canonical(rate.Amount))));
        if (amounts.Exempt > 0m) lines.Add(new(Localized("Preview.Exempt"), Canonical(amounts.Exempt)));
        if (amounts.NotTaxed > 0m) lines.Add(new(Localized("Preview.NotTaxed"), Canonical(amounts.NotTaxed)));
        return lines;
    }

    private static string Canonical(decimal value) => value.ToString("0.############################", CultureInfo.InvariantCulture);

    private static StructuredRecordResult Result(ArcaVoucherResponse voucher)
    {
        var name = Localized(voucher.Kind == ArcaVoucherKind.CreditNote ? "Voucher.CreditNote" : "Voucher.Invoice", voucher.Class.ToString());
        var receiver = voucher.Receiver ?? throw new InvalidOperationException("An issued ARCA voucher has no receiver.");
        var values = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["voucher"] = name["es"],
            ["number"] = Number(voucher),
            ["issuedOn"] = voucher.IssuedOn!.Value.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
            ["cae"] = voucher.Cae!,
            ["caeDueOn"] = voucher.CaeDueOn!.Value.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
            ["customer"] = receiver.Name,
            ["customerDocument"] = ArcaFiscalDocumentPolicy.ReceiverDocument(receiver),
            ["customerVatCondition"] = ArcaFiscalDocumentPolicy.ReceiverVatCondition(receiver),
            ["total"] = voucher.Total.ToString("0.############################", CultureInfo.InvariantCulture),
        };
        return new(ArcaInvoiceStandard.Key, Localized("Result.Source"),
            ResultFields.Select(field => new StructuredRecordResultValue(field.Key, field.Label, field.Kind, values[field.Key])).ToArray());
    }

    private static string Number(ArcaVoucherResponse voucher) => ArcaFiscalDocumentPolicy.FormatNumber(voucher.PointOfSale, voucher.Number!.Value);

    private static Dictionary<string, string> Localized(string key, params object[] arguments) => Cultures.ToDictionary(culture => culture, culture =>
        string.Format(CultureInfo.GetCultureInfo(culture), Texts.GetString(key, CultureInfo.GetCultureInfo(culture))
            ?? throw new InvalidOperationException("The ARCA invoicing text is missing."), arguments), StringComparer.Ordinal);

    private static string Camel(string name) => char.ToLowerInvariant(name[0]) + name[1..];
}
