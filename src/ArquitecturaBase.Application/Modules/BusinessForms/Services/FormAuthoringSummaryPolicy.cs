using System.Globalization;
using ArquitecturaBase.Application.Models.StructuredData;
using ArquitecturaBase.Application.Modules.BusinessForms.Models;
using ArquitecturaBase.Application.Modules.BusinessForms.Resources;

namespace ArquitecturaBase.Application.Modules.BusinessForms.Services;

// IA3-A: a channel shows the proposal itself as its preview, in the person's language. A form lists up to ten questions with
// their type and counts the rest; a catalog says in one line what its code and options are and which fields it has.
internal static class FormAuthoringSummaryPolicy
{
    public const int MaximumLines = 10;

    public static StructuredAuthoringSummary Form(FormAuthoringFormProposal proposal, string culture)
    {
        var lines = proposal.Questions.Take(MaximumLines).Select((question, index) => string.Format(CultureInfo.InvariantCulture, "{0}. {1} · {2}",
            index + 1, question.Label, FormAssistantTexts.AuthoringQuestionType(culture, question.Type, question.Catalog?.Name))).ToArray();
        return new(StructuredAuthoringKind.Form, $"{proposal.Name} · {proposal.Prefix}", lines, proposal.Questions.Count - lines.Length);
    }

    public static StructuredAuthoringSummary Catalog(FormAuthoringCatalogProposal proposal, string culture)
    {
        var parts = new List<string> { FormAssistantTexts.AuthoringCatalogCode(culture, proposal.Code), FormAssistantTexts.AuthoringCatalogOption(culture, proposal.Option) };
        if (proposal.Fields.Count > 0) parts.Add(FormAssistantTexts.AuthoringCatalogFields(culture, proposal.Fields.Count, Join(culture, proposal.Fields.Select(field => field.Name).ToArray())));
        return new(StructuredAuthoringKind.Catalog, proposal.Name, [Join(culture, parts)], 0);
    }

    // Why nothing can be built; a form or catalog that exists comes with the path to change it in the app.
    public static StructuredAuthoringSummary Declined(FormAuthoringDecline decline) => new(StructuredAuthoringKind.Declined, decline.Name, [], 0)
    {
        Reason = decline.Reason,
        AppPath = decline.DefinitionId is { } definitionId ? FormPath(definitionId) : decline.CatalogId is { } catalogId ? CatalogPath(catalogId) : null,
    };

    public static string FormPath(Guid definitionId) => $"/formularios/{definitionId:D}";
    public static string CatalogPath(Guid catalogId) => $"/catalogos/{catalogId:D}";

    // A people catalog (H4) says what its description says about who is in it.
    public static StructuredAuthoringSummary People(FormCatalogProposalPayload proposal, string culture) =>
        new(StructuredAuthoringKind.Catalog, proposal.Name, [proposal.Description.Trim().TrimEnd('.')], 0);

    // «a, b y c»: the pair word goes before the last one only.
    private static string Join(string culture, IReadOnlyList<string> items) => items.Count == 1 ? items[0]
        : FormAssistantTexts.AuthoringPair(culture, string.Join(", ", items.Take(items.Count - 1)), items[^1]);
}
