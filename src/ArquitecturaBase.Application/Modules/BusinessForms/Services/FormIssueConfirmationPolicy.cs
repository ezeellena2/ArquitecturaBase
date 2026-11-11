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

    public static string Body(StructuredIssuePreview preview, string culture)
    {
        var total = decimal.TryParse(preview.Total, NumberStyles.AllowLeadingSign | NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out var amount)
            ? amount.ToString("N2", CultureInfo.GetCultureInfo(culture == "en" ? "en-US" : "es-AR")) : preview.Total ?? "";
        var document = preview.Document?.GetValueOrDefault(culture) ?? preview.Document?.Values.FirstOrDefault() ?? "";
        return string.Format(CultureInfo.GetCultureInfo(culture), FormResponseTexts.Get("Issue.Confirm.Body", culture), document, preview.Receiver ?? "", total);
    }

    public static IReadOnlyList<StructuredRecordActionChoice> Choices(string culture) =>
        [new(Issue, FormResponseTexts.Get("Issue.Confirm.Issue", culture)), new(Skip, FormResponseTexts.Get("Issue.Confirm.Skip", culture))];

    public static string NotIssued(string culture) => FormResponseTexts.Get("Issue.NotIssued", culture);
}
