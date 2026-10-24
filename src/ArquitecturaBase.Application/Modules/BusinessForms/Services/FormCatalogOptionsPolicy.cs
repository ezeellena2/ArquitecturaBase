using System.Globalization;
using ArquitecturaBase.Application.Models.StructuredData;
using ArquitecturaBase.Application.Modules.BusinessForms.Interfaces.Persistence;
using ArquitecturaBase.Application.Modules.BusinessForms.Models;
using ArquitecturaBase.Domain.Modules.BusinessForms;

namespace ArquitecturaBase.Application.Modules.BusinessForms.Services;

internal sealed class FormCatalogOptionsPolicy(IFormCatalogReader catalogs, IFormCatalogFilterReader filters, IFormPersonDataReader people)
{
    private static readonly string[] Cultures = ["es", "en"];

    public async Task<StructuredFormPreparationResponse> PrepareAsync(Guid recipient, string culture,
        FormVersionSchema schema, string? originalCulture, CancellationToken ct, bool inspectOnly = false,
        FormPersonRecipientDataRow? recipientData = null)
    {
        var questions = schema.Questions.Where(question => question.Source?.Catalog is not null).ToArray();
        var offers = new List<StructuredFormCatalogOffer>();
        var issues = new List<StructuredFormPreparationIssue>();
        var personData = recipientData;
        foreach (var question in questions)
        {
            var id = question.Source!.Catalog!.Value;
            var catalog = await catalogs.FindAsync(id, ct);
            var label = FormRecordPresentationPolicy.Text(question.Label, culture);
            if (catalog is not { IsActive: true })
            {
                issues.Add(new(question.FieldId, label, FormRecordErrors.CatalogUnavailable.Code));
                continue;
            }
            if (catalog.Kind != "Manual" && (question.Source.Match is { Count: > 0 } || question.Source.Presentation is not null))
            {
                issues.Add(new(question.FieldId, label, FormRecordErrors.CatalogFilterUnavailable.Code));
                continue;
            }
            var fields = FormCatalogFieldPolicy.ReadFields(catalog.FieldsJson);
            if (question.Source.Match?.Any(match => match.PersonAttributeId is not null) == true)
                personData ??= await people.FindRecipientDataAsync(recipient, ct);
            var personValues = personData is null ? null : FormCatalogMatchPolicy.RecipientValues(personData);
            var containment = FormCatalogMatchPolicy.Containment(question.Source.Match, fields, personData?.Attributes ?? [], personValues);
            if (containment.IsFailure)
            {
                issues.Add(new(question.FieldId, label, containment.Error.Code, MissingData: containment.Error.Code == FormRecordErrors.RecipientDataMissing.Code
                    ? MissingData(personData!, FormCatalogMatchPolicy.MissingPersonAttribute(question.Source.Match, personValues)!.Value) : null));
                continue;
            }
            var presentation = question.Source.Presentation ?? (catalog.PresentationJson is null ? null
                : FormDraftPolicy.Read<FormCatalogPresentation>(catalog.PresentationJson));
            if (!FormCatalogPresentationPolicy.ValidConfiguration(presentation, fields, catalog.ValueType))
            {
                issues.Add(new(question.FieldId, label, FormRecordErrors.CatalogFilterUnavailable.Code));
                continue;
            }
            var valueType = catalog.Kind == "Manual" && catalog.ValueType != "Text" ? catalog.ValueType : null;
            IReadOnlyList<StructuredFormCatalogOption> options;
            if (catalog.Kind == "Manual")
            {
                var items = await catalogs.ListOfferItemsAsync(id, ct, containment.Value);
                HashSet<string> selected = presentation is null ? new(StringComparer.Ordinal) : presentation.Title.Concat(presentation.Description).ToHashSet(StringComparer.Ordinal);
                var sources = await FormCatalogFieldPresentation.SourcesAsync(fields.Where(field => selected.Contains(field.Id.ToString("D"))).ToArray(), catalogs, ct);
                var sourceItems = sources.Values.SelectMany(items => items).ToDictionary(item => item.Id);
                options = items.Where(item => item.IsActive).OrderBy(item => item.Position).ThenBy(item => item.Id)
                    .Select(item =>
                    {
                        var shown = presentation is null ? null : FormCatalogPresentationPolicy.Compose(presentation,
                            FormCatalogFieldPresentation.Values(item, fields, sourceItems, culture, catalog.ValueType));
                        return new StructuredFormCatalogOption(item.Id, item.TextFor(culture), item.DetailFor(culture), ManualTexts(item),
                            valueType is null ? null : item.Value, shown is null ? null : new(shown.Title, shown.Description));
                    }).ToArray();
            }
            else
            {
                var definition = FormDraftPolicy.Read<FormCatalogFilterDefinition>(catalog.FiltersJson!);
                var preview = await filters.FindPreviewAsync(recipient, definition, ct);
                if (preview.ExceedsLimit)
                {
                    issues.Add(new(question.FieldId, label, question.Selection is null
                        ? FormRecordErrors.CatalogTooLarge.Code : FormRecordErrors.CatalogTooLargeForMultiple.Code));
                    continue;
                }
                options = preview.Items.Select(item => new StructuredFormCatalogOption(item.Id,
                    definition.TextColumn == "active" ? FormCatalogOfferPolicy.LocalizeBoolean(item.Text, culture) : item.Text,
                    definition.DetailColumn == "active" && item.Detail is not null
                        ? FormCatalogOfferPolicy.LocalizeBoolean(item.Detail, culture) : item.Detail, FilterTexts(item, definition))).ToArray();
            }
            var offer = new StructuredFormCatalogOffer(question.FieldId, question.Key, label, id, catalog.Revision, catalog.Kind,
                Name(catalog.NameJson, culture, originalCulture), options,
                FormDraftPolicy.Read<Dictionary<string, string>>(catalog.NameJson), valueType);
            var issue = FormCatalogOfferPolicy.InvalidValues(offer) ? FormRecordErrors.CatalogOptionInvalid.Code
                : inspectOnly ? null : FormCatalogOfferPolicy.Validate(question.Required, options, question.Selection);
            if (issue is not null) issues.Add(new(question.FieldId, label, issue, offer.CatalogName,
                issue == FormRecordErrors.CatalogOptionInvalid.Code ? options.Count(option => FormCatalogOfferPolicy.IsInvalid(option)
                    || FormCatalogOfferPolicy.InvalidValue(option, valueType)) : null));
            else offers.Add(offer);
        }
        return new(offers, issues) { Culture = culture };
    }
    public static StructuredFormPreparationResponse ValidatePrepared(StructuredFormPreparationResponse prepared, FormVersionSchema schema)
    {
        if (!prepared.CanSend) return prepared;
        var questions = schema.Questions.ToDictionary(question => question.FieldId);
        var offers = new List<StructuredFormCatalogOffer>();
        var issues = new List<StructuredFormPreparationIssue>();
        foreach (var offer in prepared.Catalogs)
        {
            var question = questions[offer.FieldId];
            var code = FormCatalogOfferPolicy.InvalidValues(offer) ? FormRecordErrors.CatalogOptionInvalid.Code
                : FormCatalogOfferPolicy.Validate(question.Required, offer.Options, question.Selection);
            if (code is null) offers.Add(offer);
            else issues.Add(new(offer.FieldId, offer.Label, code, offer.CatalogName,
                code == FormRecordErrors.CatalogOptionInvalid.Code ? offer.Options.Count(option => FormCatalogOfferPolicy.IsInvalid(option)
                    || FormCatalogOfferPolicy.InvalidValue(option, offer.ValueType)) : null));
        }
        return prepared with { Catalogs = offers, Issues = issues };
    }

    // The datum's name is for the operator, so it follows the menu language rather than the form's.
    private static string MissingData(FormPersonRecipientDataRow personData, Guid attributeId) => FormRecordPresentationPolicy.Text(
        FormDraftPolicy.Read<Dictionary<string, string>>(personData.Attributes.Single(attribute => attribute.Id == attributeId).NameJson),
        CultureInfo.CurrentUICulture.TwoLetterISOLanguageName == "en" ? "en" : "es");

    private static string Name(string json, string culture, string? originalCulture)
    {
        var names = FormDraftPolicy.Read<Dictionary<string, string>>(json);
        if (names.TryGetValue(culture, out var value) && !string.IsNullOrWhiteSpace(value)) return value;
        if (originalCulture is not null && names.TryGetValue(originalCulture, out value) && !string.IsNullOrWhiteSpace(value)) return value;
        return names.OrderBy(pair => pair.Key, StringComparer.Ordinal).First(pair => !string.IsNullOrWhiteSpace(pair.Value)).Value;
    }

    // Only the cultures the option has a text in: its catalog's original and, when present, the translation.
    private static Dictionary<string, StructuredFormCatalogOptionText> ManualTexts(FormCatalogStoredItemRow item)
    {
        var result = new Dictionary<string, StructuredFormCatalogOptionText>(StringComparer.Ordinal);
        foreach (var culture in Cultures)
            if (item.TextIn(culture) is { } text) result.Add(culture, new(text, culture == "en" ? item.DetailEn : item.DetailEs));
        return result;
    }

    private static Dictionary<string, StructuredFormCatalogOptionText> FilterTexts(
        FormCatalogFilterOptionResponse item, FormCatalogFilterDefinition definition) =>
        Cultures.ToDictionary(culture => culture, culture => new StructuredFormCatalogOptionText(
            definition.TextColumn == "active" ? FormCatalogOfferPolicy.LocalizeBoolean(item.Text, culture) : item.Text,
            definition.DetailColumn == "active" && item.Detail is not null
                ? FormCatalogOfferPolicy.LocalizeBoolean(item.Detail, culture) : item.Detail), StringComparer.Ordinal);

}
