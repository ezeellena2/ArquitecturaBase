using System.Globalization;
using ArquitecturaBase.Application.Modules.BusinessForms.Interfaces.Persistence;
using ArquitecturaBase.Application.Interfaces.Integrations.Files;
using ArquitecturaBase.Application.Modules.BusinessForms.Models;
using ArquitecturaBase.Application.Resources;
using ArquitecturaBase.Domain.Modules.BusinessForms;

namespace ArquitecturaBase.Application.Modules.BusinessForms.Services;

internal sealed class FormPublicationGuard(FormPublicationAccessGuard accessGuard, IFormCatalogReader catalogReader, IPrivateFileStore files,
    Microsoft.Extensions.Options.IOptions<Configuration.BusinessFormsOptions> options, IFormPersonDataReader people)
{
    public int MaxFileSizeMiB => options.Value.MaxFileSizeMiB;
    public IReadOnlyList<FormPublicationIssueResponse> ValidateFiles(FormDefinitionResponse definition) =>
        FormPublicationPolicy.ValidateFiles(definition, files.IsAvailable);

    public Task<ArquitecturaBase.Domain.Results.Result<Guid>> CheckAsync(CancellationToken ct) => accessGuard.CheckAsync(ct);

    public async Task<IReadOnlyList<FormPublicationIssueResponse>> ValidateCatalogsAsync(IReadOnlyList<CanonicalFormField> fields, CancellationToken ct,
        IReadOnlyList<string>? cultures = null) => (await AssessCatalogsAsync(fields, false, cultures, ct)).Issues;

    public Task<FormCatalogPublicationAssessment> InspectCatalogsAsync(IReadOnlyList<CanonicalFormField> fields, CancellationToken ct,
        IReadOnlyList<string>? cultures = null) => AssessCatalogsAsync(fields, true, cultures, ct);

    private async Task<FormCatalogPublicationAssessment> AssessCatalogsAsync(IReadOnlyList<CanonicalFormField> questions, bool includeWarnings, IReadOnlyList<string>? cultures, CancellationToken ct)
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
            if (catalog is not { IsActive: true, Kind: "Manual" or "Filters" })
            {
                Issue(FormPublicationErrors.CatalogInvalid, ".catalog");
                continue;
            }
            var matches = source.Match;
            if (catalog.Kind != "Manual")
            {
                if (matches is { Count: > 0 } || source.Presentation is not null) Issue(FormPublicationErrors.MatchInvalid, ".match");
                continue;
            }
            var fields = FormCatalogFieldPolicy.ReadFields(catalog.FieldsJson);
            if (matches?.Any(match => match.PersonAttributeId is not null) == true)
                attributes ??= await people.ListAttributesAsync(ct);
            if (FormCatalogMatchPolicy.Validate(matches, fields, attributes ?? []) is { } invalid)
            {
                Issue(invalid, ".match");
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
                    if (shown.Any(option => option.Title.Length > 30))
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
            if (!includeWarnings) continue;
            var fixedContainment = FormCatalogMatchPolicy.FixedContainment(matches, fields).Value;
            var personFields = matches.Where(match => match.PersonAttributeId is not null).Select(match => match.FieldId).ToArray();
            var maximum = await catalogReader.FindMaximumOfferSizeAsync(catalogId, fixedContainment, personFields, ct);
            string? maximumLabel = null;
            if (maximum.Maximum > 0 && personFields.Length > 0)
            {
                foreach (var sourceId in fields.Where(field => personFields.Contains(field.Id) && field.SourceCatalogId is not null)
                    .Select(field => field.SourceCatalogId!.Value).Distinct())
                    if (!sourceItems.ContainsKey(sourceId)) sourceItems.Add(sourceId, await catalogReader.ListAllItemsAsync(sourceId, ct));
                var labels = FormCatalogFieldPresentation.Values(new(Guid.Empty, catalogId, "", null, null, null, null, 0, true, FieldValuesJson: maximum.ValuesJson),
                    fields, sourceItems.Values.SelectMany(values => values).ToDictionary(item => item.Id), culture, catalog.ValueType);
                maximumLabel = string.Join(" · ", personFields.Order().Select(id => labels.GetValueOrDefault(id.ToString("D"))).Where(value => !string.IsNullOrEmpty(value)));
                if (maximumLabel.Length == 0) maximumLabel = null;
            }
            warnings.Add(new(question.FieldId, path + ".match", FormPublicationErrors.OptionsPerRecipientHigh.Code,
                ErrorMessages.Get(FormPublicationErrors.OptionsPerRecipientHigh.Code), limit, maximum.Maximum, maximumLabel));
            var attributeIds = matches.Where(match => match.PersonAttributeId is not null).Select(match => match.PersonAttributeId!.Value).Distinct().ToArray();
            if (attributeIds.Length == 0) continue;
            var missing = await people.FindRecipientsWithoutDataAsync(attributeIds, ct);
            if (missing.Count > 0) warnings.Add(new(question.FieldId, path + ".match", FormPublicationErrors.RecipientsWithoutData.Code,
                ErrorMessages.Get(FormPublicationErrors.RecipientsWithoutData.Code), Count: missing.Count, People: missing.People.Take(5).ToArray()));
        }
        return new(issues.AsReadOnly(), warnings.AsReadOnly());
    }
}
