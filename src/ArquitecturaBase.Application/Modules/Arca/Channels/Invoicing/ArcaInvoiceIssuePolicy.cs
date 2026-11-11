using System.Globalization;
using System.Resources;
using ArquitecturaBase.Application.Models.StructuredData;
using ArquitecturaBase.Application.Modules.Arca.Models.Invoicing;
using ArquitecturaBase.Application.Modules.Arca.Services.Invoicing;
using ArquitecturaBase.Domain.Modules.Arca.Invoicing;

namespace ArquitecturaBase.Application.Modules.Arca.Channels.Invoicing;

/// <summary>
/// An issuance of the queue as forms sees it. It is issued once ARCA authorized it and its fiscal PDF is stored, with the voucher,
/// its number, the CAE and its due date and the total as its result; until then it is pending, on the queue's own schedule.
/// </summary>
internal static class ArcaInvoiceIssuePolicy
{
    private static readonly ResourceManager Texts = new("ArquitecturaBase.Application.Modules.Arca.Resources.Invoicing", typeof(ArcaInvoiceIssuePolicy).Assembly);
    private static readonly string[] Cultures = ["es", "en"];
    private static readonly string[] ResultKeys = ["voucher", "number", "cae", "caeDueOn", "total"];

    public static IReadOnlyDictionary<string, string> Name { get; } = Localized("Issuer.Name");

    public static IReadOnlyList<StructuredRecordResultField> ResultFields { get; } = ResultKeys.Select(key => new StructuredRecordResultField(key,
        Localized("Result." + key), key switch
        {
            "caeDueOn" => StructuredRecordResultField.Date,
            "total" => StructuredRecordResultField.Number,
            _ => StructuredRecordResultField.Text,
        })).ToArray();

    public static StructuredIssueResponse Response(ArcaVoucherResponse voucher) => voucher.State switch
    {
        ArcaVoucherState.Authorized when voucher.PdfStorageKey is { } storageKey => new(StructuredIssueState.Issued, Revision: voucher.Revision,
            Result: Result(voucher), Document: new StructuredRecordActionDocument(storageKey,
                $"{ArcaFiscalDocumentPolicy.Name(voucher.Class, voucher.Kind)} {Number(voucher)}.pdf", "application/pdf", voucher.PdfSize!.Value, voucher.PdfSha256!)),
        ArcaVoucherState.Rejected => new(StructuredIssueState.Rejected, voucher.FailureCode, Revision: voucher.Revision),
        ArcaVoucherState.NeedsReview => new(StructuredIssueState.NeedsReview, Revision: voucher.Revision,
            ReviewReason: ArcaInvoiceStandard.Key + "." + Camel(voucher.ReviewReason!.Value.ToString())),
        ArcaVoucherState.Cancelled => new(StructuredIssueState.Cancelled, Revision: voucher.Revision),
        _ => new(StructuredIssueState.Pending, voucher.FailureCode, WaitingForProvider: voucher.WaitedForArca, NextCheckAtUtc: voucher.NextAttemptAtUtc,
            Revision: voucher.Revision, WaitingNotice: voucher.WaitedForArca
                ? Localized(voucher.Kind == ArcaVoucherKind.CreditNote ? "Notice.WaitingCreditNote" : "Notice.WaitingInvoice") : null),
    };

    /// <summary>«¿La emito?»: the voucher the system decided, the receiver and the recalculated total.</summary>
    public static StructuredIssuePreview Preview(ArcaVoucherPreviewResponse preview, string receiver) =>
        new(Localized("Voucher.Invoice", preview.Class.ToString()), receiver, preview.Total.ToString("0.############################", CultureInfo.InvariantCulture));

    private static StructuredRecordResult Result(ArcaVoucherResponse voucher)
    {
        var name = Localized(voucher.Kind == ArcaVoucherKind.CreditNote ? "Voucher.CreditNote" : "Voucher.Invoice", voucher.Class.ToString());
        var values = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["voucher"] = name["es"],
            ["number"] = Number(voucher),
            ["cae"] = voucher.Cae!,
            ["caeDueOn"] = voucher.CaeDueOn!.Value.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
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
