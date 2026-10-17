using ArquitecturaBase.Application.Models.StructuredData;
using ArquitecturaBase.Application.Modules.BusinessForms.Interfaces.Persistence;
using ArquitecturaBase.Application.Modules.BusinessForms.Models;
using ArquitecturaBase.Domain.Modules.BusinessForms;

namespace ArquitecturaBase.Application.Modules.BusinessForms.Services;

internal sealed class FormCatalogPreparationPolicy(IFormRecordRepository records, IFormCatalogRepository catalogs,
    IFormCatalogFilterReader filters)
{
    private static readonly string[] Cultures = ["es", "en"];

    public async Task<StructuredFormPreparationResponse> PrepareAsync(Guid recipient, string culture,
        Guid definitionId, Guid versionId, bool forSending, CancellationToken ct)
    {
        var version = await records.GetVersionAsync(versionId, ct);
        if (version is null || version.DefinitionId != definitionId)
            return new([], [new(Guid.Empty, "", FormRecordErrors.InvalidResponse.Code)]);
        var schema = FormDraftPolicy.Read<FormVersionSchema>(version.SchemaJson);
        var questions = schema.Questions.Where(question => question.Source?.Catalog is not null).ToArray();
        if (forSending)
            foreach (var id in questions.Select(question => question.Source!.Catalog!.Value).Distinct().Order())
                await catalogs.LockAsync(id, ct);
        var offers = new List<StructuredFormCatalogOffer>();
        var issues = new List<StructuredFormPreparationIssue>();
        foreach (var question in questions)
        {
            var id = question.Source!.Catalog!.Value;
            var catalog = await catalogs.GetAsync(id, ct);
            var label = question.Label[culture];
            if (catalog is not { IsActive: true })
            {
                issues.Add(new(question.FieldId, label, FormRecordErrors.CatalogUnavailable.Code));
                continue;
            }
            IReadOnlyList<StructuredFormCatalogOption> options;
            if (catalog.Kind == BusinessFormCatalogKind.Manual)
                options = (await catalogs.ListItemsAsync(id, ct)).Where(item => item.IsActive)
                    .OrderBy(item => item.Position).ThenBy(item => item.Id)
                    .Select(item => new StructuredFormCatalogOption(item.Id, item.TextFor(culture), item.DetailFor(culture), ManualTexts(item))).ToArray();
            else
            {
                var definition = FormDraftPolicy.Read<FormCatalogFilterDefinition>(catalog.FiltersJson!);
                var preview = await filters.FindPreviewAsync(recipient, definition, ct);
                if (preview.ExceedsLimit)
                {
                    issues.Add(new(question.FieldId, label, FormRecordErrors.CatalogTooLarge.Code));
                    continue;
                }
                options = preview.Items.Select(item => new StructuredFormCatalogOption(item.Id,
                    definition.TextColumn == "active" ? FormCatalogOfferPolicy.LocalizeBoolean(item.Text, culture) : item.Text,
                    definition.DetailColumn == "active" && item.Detail is not null
                        ? FormCatalogOfferPolicy.LocalizeBoolean(item.Detail, culture) : item.Detail, FilterTexts(item, definition))).ToArray();
            }
            var issue = FormCatalogOfferPolicy.Validate(question.Required, options, version.Culture);
            if (issue is not null) issues.Add(new(question.FieldId, label, issue,
                FormDraftPolicy.Read<Dictionary<string, string>>(catalog.NameJson)[culture],
                issue == FormRecordErrors.CatalogOptionInvalid.Code ? options.Count(option => FormCatalogOfferPolicy.IsInvalid(option) || FormCatalogOfferPolicy.IsMissingCulture(option, version.Culture)) : null));
            else offers.Add(new(question.FieldId, question.Key, label, id, catalog.Revision, catalog.Kind.ToString(),
                FormDraftPolicy.Read<Dictionary<string, string>>(catalog.NameJson)[culture], options,
                FormDraftPolicy.Read<Dictionary<string, string>>(catalog.NameJson)));
        }
        return new(offers, issues);
    }
    private static Dictionary<string, StructuredFormCatalogOptionText> ManualTexts(BusinessFormCatalogItem item)
    {
        var result = new Dictionary<string, StructuredFormCatalogOptionText>(StringComparer.Ordinal)
        {
            ["es"] = new(item.TextEs, item.DetailEs)
        };
        if (!string.IsNullOrWhiteSpace(item.TextEn)) result.Add("en", new(item.TextEn, item.DetailEn));
        return result;
    }

    private static Dictionary<string, StructuredFormCatalogOptionText> FilterTexts(
        FormCatalogFilterOptionResponse item, FormCatalogFilterDefinition definition) =>
        Cultures.ToDictionary(culture => culture, culture => new StructuredFormCatalogOptionText(
            definition.TextColumn == "active" ? FormCatalogOfferPolicy.LocalizeBoolean(item.Text, culture) : item.Text,
            definition.DetailColumn == "active" && item.Detail is not null
                ? FormCatalogOfferPolicy.LocalizeBoolean(item.Detail, culture) : item.Detail), StringComparer.Ordinal);

}
