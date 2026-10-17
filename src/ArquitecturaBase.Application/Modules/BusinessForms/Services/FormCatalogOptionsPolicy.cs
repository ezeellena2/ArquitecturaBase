using ArquitecturaBase.Application.Models.StructuredData;
using ArquitecturaBase.Application.Modules.BusinessForms.Interfaces.Persistence;
using ArquitecturaBase.Application.Modules.BusinessForms.Models;
using ArquitecturaBase.Domain.Modules.BusinessForms;

namespace ArquitecturaBase.Application.Modules.BusinessForms.Services;

internal sealed class FormCatalogOptionsPolicy(IFormCatalogReader catalogs, IFormCatalogFilterReader filters)
{
    private static readonly string[] Cultures = ["es", "en"];

    public async Task<StructuredFormPreparationResponse> PrepareAsync(Guid recipient, string culture,
        FormVersionSchema schema, string? originalCulture, CancellationToken ct)
    {
        var questions = schema.Questions.Where(question => question.Source?.Catalog is not null).ToArray();
        var offers = new List<StructuredFormCatalogOffer>();
        var issues = new List<StructuredFormPreparationIssue>();
        foreach (var question in questions)
        {
            var id = question.Source!.Catalog!.Value;
            var catalog = await catalogs.FindAsync(id, ct);
            var label = question.Label[culture];
            if (catalog is not { IsActive: true })
            {
                issues.Add(new(question.FieldId, label, FormRecordErrors.CatalogUnavailable.Code));
                continue;
            }
            IReadOnlyList<StructuredFormCatalogOption> options;
            if (catalog.Kind == "Manual")
                options = (await catalogs.ListOfferItemsAsync(id, ct)).Where(item => item.IsActive)
                    .OrderBy(item => item.Position).ThenBy(item => item.Id)
                    .Select(item => new StructuredFormCatalogOption(item.Id, culture == "en" && !string.IsNullOrWhiteSpace(item.TextEn) ? item.TextEn : item.TextEs,
                        culture == "en" && !string.IsNullOrWhiteSpace(item.DetailEn) ? item.DetailEn : item.DetailEs, ManualTexts(item))).ToArray();
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
            var issue = FormCatalogOfferPolicy.Validate(question.Required, options, originalCulture);
            if (issue is not null) issues.Add(new(question.FieldId, label, issue,
                Name(catalog.NameJson, culture, originalCulture),
                issue == FormRecordErrors.CatalogOptionInvalid.Code ? options.Count(option => FormCatalogOfferPolicy.IsInvalid(option) || FormCatalogOfferPolicy.IsMissingCulture(option, originalCulture)) : null));
            else offers.Add(new(question.FieldId, question.Key, label, id, catalog.Revision, catalog.Kind,
                Name(catalog.NameJson, culture, originalCulture), options,
                FormDraftPolicy.Read<Dictionary<string, string>>(catalog.NameJson)));
        }
        return new(offers, issues);
    }
    private static string Name(string json, string culture, string? originalCulture)
    {
        var names = FormDraftPolicy.Read<Dictionary<string, string>>(json);
        if (names.TryGetValue(culture, out var value) && !string.IsNullOrWhiteSpace(value)) return value;
        if (originalCulture is not null && names.TryGetValue(originalCulture, out value) && !string.IsNullOrWhiteSpace(value)) return value;
        return names.OrderBy(pair => pair.Key, StringComparer.Ordinal).First(pair => !string.IsNullOrWhiteSpace(pair.Value)).Value;
    }

    private static Dictionary<string, StructuredFormCatalogOptionText> ManualTexts(FormCatalogStoredItemRow item)
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
