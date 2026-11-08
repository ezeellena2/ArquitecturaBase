using System.Globalization;
using ArquitecturaBase.Application.Models.StructuredData;
using ArquitecturaBase.Application.Modules.BusinessForms.Interfaces.Persistence;
using ArquitecturaBase.Application.Modules.BusinessForms.Models;
using ArquitecturaBase.Domain.Modules.BusinessForms;

namespace ArquitecturaBase.Application.Modules.BusinessForms.Services;

internal sealed class FormCatalogOptionsPolicy(IFormCatalogReader catalogs, IFormCatalogFilterReader filters, IFormPersonDataReader people)
{
    private static readonly string[] Cultures = ["es", "en"];

    /// <summary>How many options of each list the official preview offers (VO1-A).</summary>
    public const int PreviewOptionLimit = 20;

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
            var label = FormRecordPresentationPolicy.Text(question.Label, culture);
            var source = await ResolveAsync(recipient, question, personData, ct);
            personData = source.PersonData;
            if (source.Issue is { } code)
            {
                issues.Add(new(question.FieldId, label, code, MissingData: source.MissingData));
                continue;
            }
            var catalog = source.Catalog!;
            var valueType = ValueType(catalog);
            IReadOnlyList<StructuredFormCatalogOption> options;
            StructuredFormCatalogGrouping? grouping = null;
            if (catalog.Kind == "Manual")
            {
                // LD2-A: a question that chooses first by a field needs that field, and all its options, which are limited per value.
                var groupBy = GroupByField(question, source.Fields);
                if (question.Source!.GroupBy is not null && groupBy is null)
                {
                    issues.Add(new(question.FieldId, label, FormRecordErrors.CatalogFilterUnavailable.Code));
                    continue;
                }
                var items = await catalogs.ListOfferItemsAsync(catalog.Id, ct, source.Containment, groupBy is null ? OfferReadLimit : BusinessFormCatalog.MaxItems);
                options = await ManualOptionsAsync(source, items, culture, ct);
                if (groupBy is not null) (options, grouping) = await GroupAsync(groupBy, items, options, culture, ct);
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
                options = FilterOptions(preview.Items, definition, culture);
            }
            var offer = new StructuredFormCatalogOffer(question.FieldId, question.Key, label, catalog.Id, catalog.Revision, catalog.Kind,
                Name(catalog.NameJson, culture, originalCulture), options,
                FormDraftPolicy.Read<Dictionary<string, string>>(catalog.NameJson), valueType, grouping);
            var issue = FormCatalogOfferPolicy.InvalidValues(offer) ? FormRecordErrors.CatalogOptionInvalid.Code
                : inspectOnly ? null : FormCatalogOfferPolicy.Validate(question.Required, options, question.Selection, grouping);
            if (issue is not null) issues.Add(new(question.FieldId, label, issue, offer.CatalogName,
                issue == FormRecordErrors.CatalogOptionInvalid.Code ? options.Count(option => FormCatalogOfferPolicy.IsInvalid(option)
                    || FormCatalogOfferPolicy.InvalidValue(option, valueType)) : null));
            else offers.Add(offer);
        }
        return new(offers, issues) { Culture = culture };
    }
    /// <summary>
    /// VO1-A (2026-11-06): sample options for the official preview of a published version, so it can be gone through like on the
    /// phone: the first <see cref="PreviewOptionLimit"/> options of each catalog question, in order, with their shown texts in
    /// <paramref name="culture"/>. The preview is for nobody in particular: without person filters or filters by another answer,
    /// while fixed filters still apply; a people list is the one <paramref name="viewer"/> would get. A catalog that cannot be
    /// offered is left out, so its list stays empty.
    /// </summary>
    public async Task<IReadOnlyList<StructuredFormCatalogOffer>> PreviewAsync(Guid viewer, string culture, FormVersionSchema schema,
        string? originalCulture, CancellationToken ct)
    {
        var offers = new List<StructuredFormCatalogOffer>();
        foreach (var question in schema.Questions.Where(question => question.Source?.Catalog is not null))
        {
            var catalog = await catalogs.FindAsync(question.Source!.Catalog!.Value, ct);
            if (catalog is not { IsActive: true }) continue;
            IReadOnlyList<StructuredFormCatalogOption> options;
            if (catalog.Kind == "Manual")
            {
                var fields = FormCatalogFieldPolicy.ReadFields(catalog.FieldsJson);
                var containment = FormCatalogMatchPolicy.FixedContainment(question.Source.Match, fields);
                var presentation = question.Source.Presentation ?? (catalog.PresentationJson is null ? null
                    : FormDraftPolicy.Read<FormCatalogPresentation>(catalog.PresentationJson));
                if (containment.IsFailure || !FormCatalogPresentationPolicy.ValidConfiguration(presentation, fields, catalog.ValueType)) continue;
                var source = new FormCatalogQuestionSource(catalog, fields, presentation, containment.Value, null, null, null);
                var groupBy = GroupByField(question, fields);
                if (question.Source.GroupBy is not null && groupBy is null) continue;
                var items = await catalogs.ListOfferItemsAsync(catalog.Id, ct, containment.Value, groupBy is null ? OfferReadLimit : BusinessFormCatalog.MaxItems);
                options = await ManualOptionsAsync(source, items, culture, ct);
                if (groupBy is not null)
                {
                    // LD2-A: the first values, each with its first options.
                    var (grouped, grouping) = await GroupAsync(groupBy, items, options, culture, ct);
                    var shown = grouping.Groups.Take(PreviewOptionLimit).ToArray();
                    var kept = shown.SelectMany(group => grouped.Where(option => option.Group == group.Value).Take(PreviewOptionLimit)).Select(option => option.Id).ToHashSet();
                    offers.Add(new(question.FieldId, question.Key, FormRecordPresentationPolicy.Text(question.Label, culture), catalog.Id, catalog.Revision,
                        catalog.Kind, Name(catalog.NameJson, culture, originalCulture), grouped.Where(option => kept.Contains(option.Id)).ToArray(),
                        FormDraftPolicy.Read<Dictionary<string, string>>(catalog.NameJson), ValueType(catalog), grouping with { Groups = shown }));
                    continue;
                }
            }
            else
            {
                if (question.Source.Match is { Count: > 0 } || question.Source.Presentation is not null) continue;
                var definition = FormDraftPolicy.Read<FormCatalogFilterDefinition>(catalog.FiltersJson!);
                options = FilterOptions((await filters.FindPreviewAsync(viewer, definition, ct)).Items, definition, culture);
            }
            offers.Add(new(question.FieldId, question.Key, FormRecordPresentationPolicy.Text(question.Label, culture), catalog.Id, catalog.Revision,
                catalog.Kind, Name(catalog.NameJson, culture, originalCulture), options.Take(PreviewOptionLimit).ToArray(),
                FormDraftPolicy.Read<Dictionary<string, string>>(catalog.NameJson), ValueType(catalog)));
        }
        return offers;
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
                : FormCatalogOfferPolicy.Validate(question.Required, offer.Options, question.Selection, offer.Grouping);
            if (code is null) offers.Add(offer);
            else issues.Add(new(offer.FieldId, offer.Label, code, offer.CatalogName,
                code == FormRecordErrors.CatalogOptionInvalid.Code ? offer.Options.Count(option => FormCatalogOfferPolicy.IsInvalid(option)
                    || FormCatalogOfferPolicy.InvalidValue(option, offer.ValueType)) : null));
        }
        return prepared with { Catalogs = offers, Issues = issues };
    }

    /// <summary>
    /// The catalog of a question with its fields, presentation and the containment of its fixed and person filters for
    /// <paramref name="recipient"/>, or the issue that blocks it. Person data are read once and travel in the result. A filter by
    /// another answer takes it from <paramref name="answers"/>; without it the question is blocked (a channel cannot ask it).
    /// </summary>
    public async Task<FormCatalogQuestionSource> ResolveAsync(Guid recipient, CanonicalFormField question,
        FormPersonRecipientDataRow? personData, CancellationToken ct, IReadOnlyDictionary<Guid, Guid>? answers = null)
    {
        var catalog = await catalogs.FindAsync(question.Source!.Catalog!.Value, ct);
        if (catalog is not { IsActive: true }) return FormCatalogQuestionSource.Blocked(FormRecordErrors.CatalogUnavailable.Code, personData);
        if (catalog.Kind != "Manual" && (question.Source.Match is { Count: > 0 } || question.Source.Presentation is not null))
            return FormCatalogQuestionSource.Blocked(FormRecordErrors.CatalogFilterUnavailable.Code, personData);
        var fields = FormCatalogFieldPolicy.ReadFields(catalog.FieldsJson);
        if (question.Source.Match?.Any(match => match.PersonAttributeId is not null) == true)
            personData ??= await people.FindRecipientDataAsync(recipient, ct);
        var personValues = personData is null ? null : FormCatalogMatchPolicy.RecipientValues(personData);
        var containment = FormCatalogMatchPolicy.Containment(question.Source.Match, fields, personData?.Attributes ?? [], personValues, answers);
        if (containment.IsFailure)
            return FormCatalogQuestionSource.Blocked(containment.Error.Code, personData, containment.Error.Code == FormRecordErrors.RecipientDataMissing.Code
                ? MissingData(personData!, FormCatalogMatchPolicy.MissingPersonAttribute(question.Source.Match, personValues)!.Value) : null);
        var presentation = question.Source.Presentation ?? (catalog.PresentationJson is null ? null
            : FormDraftPolicy.Read<FormCatalogPresentation>(catalog.PresentationJson));
        if (!FormCatalogPresentationPolicy.ValidConfiguration(presentation, fields, catalog.ValueType))
            return FormCatalogQuestionSource.Blocked(FormRecordErrors.CatalogFilterUnavailable.Code, personData);
        return new(catalog, fields, presentation, containment.Value, personData, null, null);
    }

    /// <summary>The active items as offered options, in order, with their texts, value and shown title and description in <paramref name="culture"/>.</summary>
    public async Task<IReadOnlyList<StructuredFormCatalogOption>> ManualOptionsAsync(FormCatalogQuestionSource source,
        IReadOnlyList<FormCatalogStoredItemRow> items, string culture, CancellationToken ct)
    {
        var catalog = source.Catalog!;
        var presentation = source.Presentation;
        var valueType = ValueType(catalog);
        HashSet<string> selected = presentation is null ? new(StringComparer.Ordinal) : presentation.Title.Concat(presentation.Description).ToHashSet(StringComparer.Ordinal);
        var sources = await FormCatalogFieldPresentation.SourcesAsync(source.Fields.Where(field => selected.Contains(field.Id.ToString("D"))).ToArray(), catalogs, ct);
        var sourceItems = sources.Values.SelectMany(values => values).ToDictionary(item => item.Id);
        return items.Where(item => item.IsActive).OrderBy(item => item.Position).ThenBy(item => item.Id)
            .Select(item =>
            {
                var shown = presentation is null ? null : FormCatalogPresentationPolicy.Compose(presentation,
                    FormCatalogFieldPresentation.Values(item, source.Fields, sourceItems, culture, catalog.ValueType));
                return new StructuredFormCatalogOption(item.Id, item.TextFor(culture), item.DetailFor(culture), ManualTexts(item),
                    valueType is null ? null : item.Value, shown is null ? null : new(shown.Title, shown.Description));
            }).ToArray();
    }

    public static IReadOnlyList<StructuredFormCatalogOption> FilterOptions(IReadOnlyList<FormCatalogFilterOptionResponse> items,
        FormCatalogFilterDefinition definition, string culture) =>
        items.Select(item => new StructuredFormCatalogOption(item.Id,
            definition.TextColumn == "active" ? FormCatalogOfferPolicy.LocalizeBoolean(item.Text, culture) : item.Text,
            definition.DetailColumn == "active" && item.Detail is not null
                ? FormCatalogOfferPolicy.LocalizeBoolean(item.Detail, culture) : item.Detail, FilterTexts(item, definition))).ToArray();

    // Enough options to tell when a list passes its limit of 200.
    private const int OfferReadLimit = 201;

    // LD1-A: the field a question chooses first by, while it is an active field of text or catalog type.
    private static BusinessFormCatalogField? GroupByField(CanonicalFormField question, IReadOnlyList<BusinessFormCatalogField> fields) =>
        question.Source?.GroupBy is { } groupBy ? fields.SingleOrDefault(field => field.Id == groupBy && field.IsActive && field.Type is "text" or "catalog") : null;

    // LD2-A: each option with its value in the field to choose first, and the values in order with their texts, as the app lists
    // them (`FormCatalogGroupPolicy.Present`), under the field's name in the offer's culture.
    private async Task<(IReadOnlyList<StructuredFormCatalogOption> Options, StructuredFormCatalogGrouping Grouping)> GroupAsync(BusinessFormCatalogField field,
        IReadOnlyList<FormCatalogStoredItemRow> items, IReadOnlyList<StructuredFormCatalogOption> options, string culture, CancellationToken ct)
    {
        var key = field.Id.ToString("D");
        var values = items.ToDictionary(item => item.Id, item => FormCatalogGroupPolicy.Value(item.FieldValuesJson, key));
        var grouped = options.Select(option => option with { Group = values[option.Id] }).ToArray();
        var rows = grouped.GroupBy(option => option.Group).Select(group => new FormEntryGroupRow(group.Key, group.Count())).ToArray();
        var sourceItems = field.Type == "catalog" && field.SourceCatalogId is { } sourceId ? await catalogs.ListAllItemsAsync(sourceId, ct) : [];
        var groups = FormCatalogGroupPolicy.Present(field, rows, sourceItems, culture).Select(group => new StructuredFormCatalogGroup(group.Id, group.Text)).ToArray();
        return (grouped, new(FormRecordPresentationPolicy.Text(field.Name, culture), groups));
    }

    /// <summary>The typed value of a Manual catalog's options; a text catalog and Filters offer none.</summary>
    public static string? ValueType(FormCatalogStoredRow catalog) => catalog.Kind == "Manual" && catalog.ValueType != "Text" ? catalog.ValueType : null;

    // The datum's name is for the operator, so it follows the menu language rather than the form's.
    private static string MissingData(FormPersonRecipientDataRow personData, Guid attributeId) => FormRecordPresentationPolicy.Text(
        FormDraftPolicy.Read<Dictionary<string, string>>(personData.Attributes.Single(attribute => attribute.Id == attributeId).NameJson),
        CultureInfo.CurrentUICulture.TwoLetterISOLanguageName == "en" ? "en" : "es");

    public static string Name(string json, string culture, string? originalCulture)
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

/// <summary>A question's catalog ready to offer, or the issue that blocks it (<see cref="Issue"/>).</summary>
internal sealed record FormCatalogQuestionSource(FormCatalogStoredRow? Catalog, IReadOnlyList<BusinessFormCatalogField> Fields,
    FormCatalogPresentation? Presentation, string? Containment, FormPersonRecipientDataRow? PersonData, string? Issue, string? MissingData)
{
    public static FormCatalogQuestionSource Blocked(string issue, FormPersonRecipientDataRow? personData, string? missingData = null) =>
        new(null, [], null, null, personData, issue, missingData);
    public override string ToString() => "FormCatalogQuestionSource { Content = [REDACTED] }";
}
