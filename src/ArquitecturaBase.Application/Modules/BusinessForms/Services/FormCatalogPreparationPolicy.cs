using ArquitecturaBase.Application.Models.StructuredData;
using ArquitecturaBase.Application.Modules.BusinessForms.Interfaces.Persistence;
using ArquitecturaBase.Application.Modules.BusinessForms.Models;
using ArquitecturaBase.Domain.Modules.BusinessForms;

namespace ArquitecturaBase.Application.Modules.BusinessForms.Services;

internal sealed class FormCatalogPreparationPolicy(IFormRecordRepository records, IFormCatalogRepository catalogs,
    IFormCatalogFilterReader filters)
{
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
            if (catalog is not { IsActive: true } || catalog.Kind == BusinessFormCatalogKind.Filters
                && !await filters.ExistsAvailableSourceAsync(ct))
            {
                issues.Add(new(question.FieldId, label, FormRecordErrors.CatalogUnavailable.Code));
                continue;
            }
            IReadOnlyList<StructuredFormCatalogOption> options;
            if (catalog.Kind == BusinessFormCatalogKind.Manual)
                options = (await catalogs.ListItemsAsync(id, ct)).Where(item => item.IsActive)
                    .OrderBy(item => item.Position).ThenBy(item => item.Id)
                    .Select(item => new StructuredFormCatalogOption(item.Id, item.TextFor(culture), item.DetailFor(culture))).ToArray();
            else
            {
                var preview = await filters.FindPreviewAsync(recipient,
                    FormDraftPolicy.Read<FormCatalogFilterDefinition>(catalog.FiltersJson!), ct);
                if (preview.ExceedsLimit)
                {
                    issues.Add(new(question.FieldId, label, FormRecordErrors.CatalogTooLarge.Code));
                    continue;
                }
                options = preview.Items.Select(item => new StructuredFormCatalogOption(item.Id, item.Text, item.Detail)).ToArray();
            }
            var issue = FormCatalogOfferPolicy.Validate(question.Required, options);
            if (issue is not null) issues.Add(new(question.FieldId, label, issue));
            else offers.Add(new(question.FieldId, question.Key, label, id, catalog.Revision, catalog.Kind.ToString(),
                FormDraftPolicy.Read<Dictionary<string, string>>(catalog.NameJson)[culture], options));
        }
        return new(offers, issues);
    }
}
