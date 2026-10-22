using ArquitecturaBase.Application.Modules.BusinessForms.Interfaces.Persistence;
using ArquitecturaBase.Application.Interfaces.Integrations.Files;
using ArquitecturaBase.Application.Modules.BusinessForms.Models;
using ArquitecturaBase.Application.Resources;
using ArquitecturaBase.Domain.Modules.BusinessForms;

namespace ArquitecturaBase.Application.Modules.BusinessForms.Services;

internal sealed class FormPublicationGuard(FormPublicationAccessGuard accessGuard, IFormCatalogReader catalogReader, IPrivateFileStore files, Microsoft.Extensions.Options.IOptions<ArquitecturaBase.Application.Modules.BusinessForms.Configuration.BusinessFormsOptions> options)
{
    public int MaxFileSizeMiB => options.Value.MaxFileSizeMiB;
    public IReadOnlyList<FormPublicationIssueResponse> ValidateFiles(FormDefinitionResponse definition) =>
        FormPublicationPolicy.ValidateFiles(definition, files.IsAvailable);

    public Task<ArquitecturaBase.Domain.Results.Result<Guid>> CheckAsync(CancellationToken cancellationToken) =>
        accessGuard.CheckAsync(cancellationToken);

    public async Task<IReadOnlyList<FormPublicationIssueResponse>> ValidateCatalogsAsync(
        IReadOnlyList<CanonicalFormField> fields, CancellationToken cancellationToken)
    {
        var catalogs = new Dictionary<Guid, FormCatalogStoredRow?>();
        var counts = new Dictionary<Guid, int>();
        var issues = new List<FormPublicationIssueResponse>();
        for (var index = 0; index < fields.Count; index++)
        {
            var field = fields[index];
            if (field.Source?.Catalog is not { } catalogId) continue;
            if (!catalogs.TryGetValue(catalogId, out var catalog))
            {
                catalog = await catalogReader.FindAsync(catalogId, cancellationToken);
                catalogs.Add(catalogId, catalog);
            }
            if (catalog is not { IsActive: true, Kind: "Manual" or "Filters" })
            {
                issues.Add(new(null, field.FieldId, null, $"draft.fields[{index}].source.catalog",
                    FormPublicationErrors.CatalogInvalid.Code,
                    ErrorMessages.Get(FormPublicationErrors.CatalogInvalid.Code), null, null));
                continue;
            }
            if (field.Type != "multiChoice" || catalog.Kind != "Manual") continue;
            if (!counts.TryGetValue(catalogId, out var count))
            {
                count = await catalogReader.CountActiveItemsAsync(catalogId, cancellationToken);
                counts.Add(catalogId, count);
            }
            if (count > 20)
                issues.Add(new(null, field.FieldId, null, $"draft.fields[{index}].source.catalog",
                    FormPublicationErrors.CatalogTooLargeForMultiple.Code,
                    ErrorMessages.Get(FormPublicationErrors.CatalogTooLargeForMultiple.Code), null, null));
        }
        return issues.AsReadOnly();
    }
}
