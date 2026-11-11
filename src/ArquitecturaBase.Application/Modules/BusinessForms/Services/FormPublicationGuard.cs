using System.Globalization;
using ArquitecturaBase.Application.Modules.BusinessForms.Interfaces.Persistence;
using ArquitecturaBase.Application.Interfaces.Integrations.Files;
using ArquitecturaBase.Application.Modules.BusinessForms.Models;
using ArquitecturaBase.Application.Resources;
using ArquitecturaBase.Domain.Modules.BusinessForms;

namespace ArquitecturaBase.Application.Modules.BusinessForms.Services;

internal sealed class FormPublicationGuard(FormPublicationAccessGuard accessGuard, IFormCatalogReader catalogReader, IPrivateFileStore files,
    Microsoft.Extensions.Options.IOptions<Configuration.BusinessFormsOptions> options, IFormPersonDataReader people,
    FormCatalogRecipientMaximumPolicy maximums)
{
    public int MaxFileSizeMiB => options.Value.MaxFileSizeMiB;
    public IReadOnlyList<FormPublicationIssueResponse> ValidateFiles(FormDefinitionResponse definition) =>
        FormPublicationPolicy.ValidateFiles(definition, files.IsAvailable);

    public Task<ArquitecturaBase.Domain.Results.Result<Guid>> CheckAsync(CancellationToken ct) => accessGuard.CheckAsync(ct);

    // forChannels is false for a publication only in the app (SA2-A): the limits of the WhatsApp list (the 30 characters of an
    // option title and its 200 options) and what cannot be sent by WhatsApp do not apply; the rules of the app stay.
    public async Task<IReadOnlyList<FormPublicationIssueResponse>> ValidateCatalogsAsync(IReadOnlyList<CanonicalFormField> fields, CancellationToken ct,
        IReadOnlyList<string>? cultures = null, bool forChannels = true) => (await AssessCatalogsAsync(fields, false, cultures, forChannels, ct)).Issues;

    public Task<FormCatalogPublicationAssessment> InspectCatalogsAsync(IReadOnlyList<CanonicalFormField> fields, CancellationToken ct,
        IReadOnlyList<string>? cultures = null, bool forChannels = true) => AssessCatalogsAsync(fields, true, cultures, forChannels, ct);

    private async Task<FormCatalogPublicationAssessment> AssessCatalogsAsync(IReadOnlyList<CanonicalFormField> questions, bool includeWarnings, IReadOnlyList<string>? cultures,
        bool forChannels, CancellationToken ct)
    {
        var catalogs = new Dictionary<Guid, FormCatalogStoredRow?>();
        var counts = new Dictionary<Guid, int>();
        var items = new Dictionary<Guid, IReadOnlyList<FormCatalogStoredItemRow>>();
        var sourceItems = new Dictionary<Guid, IReadOnlyList<FormCatalogStoredItemRow>>();
        IReadOnlyList<FormPersonAttributeStoredRow>? attributes = null;
        var issues = new List<FormPublicationIssueResponse>();
        var warnings = new List<FormPublicationWarningResponse>();
        var culture = CultureInfo.CurrentUICulture.TwoLetterISOLanguageName == "en" ? "en" : "es";
        for (var index = 0; index < questions.Count; index++)
        {
            var question = questions[index];
            if (question.Source?.Catalog is not { } catalogId) continue;
            var source = question.Source;
            var path = $"draft.fields[{index}].source";
            void Issue(ArquitecturaBase.Domain.Results.Error error, string suffix, int? limit = null, string? issueCulture = null) => issues.Add(new(null, question.FieldId, null,
                path + suffix, error.Code, ErrorMessages.Get(error.Code), limit, issueCulture));
            if (!catalogs.TryGetValue(catalogId, out var catalog))
            {
                catalog = await catalogReader.FindAsync(catalogId, ct);
                catalogs.Add(catalogId, catalog);
            }
            if (catalog is not { IsActive: true, Kind: "Manual" or "Filters" or FormCatalogKindPolicy.External })
            {
                Issue(FormPublicationErrors.CatalogInvalid, ".catalog");
                continue;
            }
            var matches = source.Match;
            if (catalog.Kind != "Manual")
            {
                if (matches is { Count: > 0 } || source.Presentation is not null) Issue(FormPublicationErrors.MatchInvalid, ".match");
                if (source.GroupBy is not null) Issue(FormPublicationErrors.GroupByInvalid, ".groupBy");
                continue;
            }
            var fields = FormCatalogFieldPolicy.ReadFields(catalog.FieldsJson);
            // LD1-A: the value chosen first is that of an active text or catalog field of the same catalog, whatever its role.
            if (source.GroupBy is { } groupBy && fields.SingleOrDefault(field => field.Id == groupBy) is not { IsActive: true, Type: "text" or "catalog" })
            {
                Issue(FormPublicationErrors.GroupByInvalid, ".groupBy");
                continue;
            }
            if (matches?.Any(match => match.PersonAttributeId is not null) == true)
                attributes ??= await people.ListAttributesAsync(ct);
            if ((FormCatalogMatchPolicy.Validate(matches, fields, attributes ?? [])
                ?? FormCatalogMatchPolicy.ValidateQuestions(question, matches, fields, questions.Take(index).ToArray())) is { } invalid)
            {
                Issue(invalid, ".match");
                continue;
            }
            if (await maximums.CheckFixedValuesAsync(fields, matches, sourceItems, ct) is { } unavailable)
            {
                Issue(unavailable, ".match");
                continue;
            }
            var presentation = source.Presentation ?? (catalog.PresentationJson is null ? null : FormDraftPolicy.Read<FormCatalogPresentation>(catalog.PresentationJson));
            if (!FormCatalogPresentationPolicy.ValidConfiguration(presentation, fields, catalog.ValueType))
            {
                Issue(FormCatalogErrors.PresentationInvalid, ".presentation");
                continue;
            }
            if (presentation is not null)
            {
                if (!items.TryGetValue(catalogId, out var allItems))
                {
                    allItems = await catalogReader.ListAllItemsAsync(catalogId, ct);
                    items.Add(catalogId, allItems);
                }
                foreach (var sourceId in fields.Where(field => field.IsActive && field.SourceCatalogId is not null)
                    .Select(field => field.SourceCatalogId!.Value).Distinct())
                    if (!sourceItems.ContainsKey(sourceId)) sourceItems.Add(sourceId, await catalogReader.ListAllItemsAsync(sourceId, ct));
                var choices = sourceItems.Values.SelectMany(values => values).ToDictionary(item => item.Id);
                foreach (var checkedCulture in cultures ?? [culture])
                {
                    var shown = allItems.Where(item => item.IsActive).Select(item => FormCatalogPresentationPolicy.Compose(presentation,
                        FormCatalogFieldPresentation.Values(item, fields, choices, checkedCulture, catalog.ValueType))).ToArray();
                    if (forChannels && shown.Any(option => option.Title.Length > 30))
                        Issue(source.Presentation is null ? FormPublicationErrors.CatalogTitleTooLong : FormPublicationErrors.QuestionTitleTooLong,
                            ".presentation", 30, checkedCulture);
                    if (shown.Any(option => option.Title.Length <= 30 && !FormCatalogPresentationPolicy.IsValid(option)))
                        Issue(FormPublicationErrors.PresentationInvalid, ".presentation", issueCulture: checkedCulture);
                }
            }
            var limit = question.Type == "multiChoice" ? 20 : 200;
            if (matches is not { Count: > 0 })
            {
                if (question.Type != "multiChoice") continue;
                if (!counts.TryGetValue(catalogId, out var count))
                {
                    count = await catalogReader.CountActiveItemsAsync(catalogId, ct);
                    counts.Add(catalogId, count);
                }
                if (count > limit) Issue(FormPublicationErrors.CatalogTooLargeForMultiple, ".catalog");
                continue;
            }
            var blocks = question.Type == "multiChoice" && matches.All(match => match.Value is not null);
            if (!includeWarnings && !blocks) continue;
            // CW7: a list that depends on another answer is loaded only in the app, so the WhatsApp limits do not apply to it.
            if (matches.Any(match => match.QuestionId is not null))
            {
                if (forChannels)
                    warnings.Add(new(question.FieldId, path + ".match", FormPublicationErrors.DependsOnAnswer.Code,
                        ErrorMessages.Get(FormPublicationErrors.DependsOnAnswer.Code)));
            }
            else
            {
                var (maximum, maximumLabel, groups, largestGroup) = await maximums.FindAsync(catalogId, fields, matches, culture, ct, source.GroupBy);
                if (blocks && maximum > limit)
                {
                    Issue(FormPublicationErrors.CatalogTooLargeForMultiple, ".catalog");
                    continue;
                }
                if (!includeWarnings) continue;
                // NI3-B (2026-11-03): like the question editor, the maximum warns only when it does not fit or nobody sees an option.
                // LD1-A: choosing first by a field, the limit applies to each value and to the list of values, not to the total.
                var largest = groups is null ? maximum : Math.Max(groups.Value, largestGroup!.Value);
                if (maximum == 0 || forChannels && largest > limit)
                    warnings.Add(new(question.FieldId, path + ".match", FormPublicationErrors.OptionsPerRecipientHigh.Code,
                        ErrorMessages.Get(FormPublicationErrors.OptionsPerRecipientHigh.Code), limit, largest, maximumLabel));
            }
            var attributeIds = matches.Where(match => match.PersonAttributeId is not null).Select(match => match.PersonAttributeId!.Value).Distinct().ToArray();
            if (attributeIds.Length == 0) continue;
            var missing = await people.FindRecipientsWithoutDataAsync(attributeIds, ct);
            if (missing.Count > 0) warnings.Add(new(question.FieldId, path + ".match", FormPublicationErrors.RecipientsWithoutData.Code,
                ErrorMessages.Get(FormPublicationErrors.RecipientsWithoutData.Code), Count: missing.Count, People: missing.People.Take(5).ToArray()));
        }
        // CA1-A: the rate of a percentage that comes from a catalog needs a catalog of numbers.
        for (var index = 0; index < questions.Count; index++)
        {
            if (questions[index].Calculation is not { Operation: FormCalculation.Percent } calculation) continue;
            if (questions.FirstOrDefault(field => field.FieldId == calculation.Operands[1]) is not { Source.Catalog: { } rateCatalog }) continue;
            if (!catalogs.TryGetValue(rateCatalog, out var rate)) { rate = await catalogReader.FindAsync(rateCatalog, ct); catalogs.Add(rateCatalog, rate); }
            if (rate?.ValueType is not ("Integer" or "Decimal"))
                issues.Add(new(null, questions[index].FieldId, null, $"draft.fields[{index}].calculation", FormPublicationErrors.CalculationInvalid.Code,
                    ErrorMessages.Get(FormPublicationErrors.CalculationInvalid.Code), null, null));
        }
        return new(issues.AsReadOnly(), warnings.AsReadOnly());
    }
}
