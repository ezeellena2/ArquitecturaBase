using System.Globalization;
using ArquitecturaBase.Application.Models.StructuredData;
using ArquitecturaBase.Application.Modules.BusinessForms.Resources;

namespace ArquitecturaBase.Application.Modules.BusinessForms.Services;

/// <summary>
/// «¿La emito?» (EM2-A): the summary of an issuance for whoever loaded the response, in their language, with the document the
/// issuer decided, the receiver and the total, and the two choices. «No emitir» voids the response with «No se emitió».
/// </summary>
internal static class FormIssueConfirmationPolicy
{
    public const string Issue = "issue", Skip = "skip";

    // As the boards EM2-A and IV3: the document, the receiver, the total with the amounts the document itemizes between
    // parentheses (the net and each tax of an invoice A, the VAT an invoice B contains), the date (dd/MM/yyyy, MM/dd/yyyy in
    // English) and the question last.
    public static string Body(StructuredIssuePreview preview, string culture)
    {
        var document = preview.Document?.GetValueOrDefault(culture) ?? preview.Document?.Values.FirstOrDefault() ?? "";
        var date = DateOnly.TryParseExact(preview.Date, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var day)
            ? day.ToString(culture == "en" ? "MM/dd/yyyy" : "dd/MM/yyyy", CultureInfo.InvariantCulture) : null;
        var amounts = (preview.Amounts ?? []).Select(item => (item.Label.GetValueOrDefault(culture) ?? item.Label.Values.First()) + ": $ " + Money(item.Value, culture)).ToArray();
        var total = Money(preview.Total, culture) + (amounts.Length == 0 ? "" : " (" + string.Join("; ", amounts) + ")");
        var summary = string.Format(CultureInfo.GetCultureInfo(culture), FormResponseTexts.Get(date is null ? "Issue.Confirm.Body" : "Issue.Confirm.BodyDated", culture),
            document, preview.Receiver ?? "", total, date);
        return summary + "\n" + FormResponseTexts.Get("Issue.Confirm.Question", culture);
    }

    private static string Money(string? value, string culture) =>
        decimal.TryParse(value, NumberStyles.AllowLeadingSign | NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out var amount)
            ? amount.ToString("N2", CultureInfo.GetCultureInfo(culture == "en" ? "en-US" : "es-AR")) : value ?? "";

    public static IReadOnlyList<StructuredRecordActionChoice> Choices(string culture) =>
        [new(Issue, FormResponseTexts.Get("Issue.Confirm.Issue", culture)), new(Skip, FormResponseTexts.Get("Issue.Confirm.Skip", culture))];

    public static string NotIssued(string culture) => FormResponseTexts.Get("Issue.NotIssued", culture);

    /// <summary>What a channel answers after «No emitir»: «No la emitimos. FAC-000012 quedó anulada.», the response named as the app names it.</summary>
    public static string Voided(string prefix, long number, string culture) =>
        string.Format(CultureInfo.InvariantCulture, FormResponseTexts.Get("Issue.Voided", culture), prefix + "-" + number.ToString("D6", CultureInfo.InvariantCulture));
}
