using ArquitecturaBase.Application.Modules.BusinessForms.Interfaces.Persistence;
using ArquitecturaBase.Application.Modules.BusinessForms.Models;
using ArquitecturaBase.Application.Resources;
using ArquitecturaBase.Domain.Modules.BusinessForms;

namespace ArquitecturaBase.Application.Modules.BusinessForms.Services;

internal sealed class FormPublicationGuard(FormPublicationAccessGuard accessGuard, IFormCatalogReader catalogReader)
{
    public Task<ArquitecturaBase.Domain.Results.Result<Guid>> CheckAsync(CancellationToken cancellationToken) =>
        accessGuard.CheckAsync(cancellationToken);

    public async Task<IReadOnlyList<FormPublicationIssueResponse>> ValidateCatalogsAsync(
        IReadOnlyList<CanonicalFormField> fields, CancellationToken cancellationToken)
    {
        var validity = new Dictionary<Guid, bool>();
        var issues = new List<FormPublicationIssueResponse>();
        for (var index = 0; index < fields.Count; index++)
        {
            if (fields[index].Source?.Catalog is not { } catalogId) continue;
            if (!validity.TryGetValue(catalogId, out var isValid))
            {
                var catalog = await catalogReader.FindAsync(catalogId, cancellationToken);
                isValid = catalog is { IsActive: true, Kind: "Manual" or "Filters" };
                validity.Add(catalogId, isValid);
            }
            if (!isValid)
                issues.Add(new(null, fields[index].FieldId, null, $"draft.fields[{index}].source.catalog",
                    FormPublicationErrors.CatalogInvalid.Code,
                    ErrorMessages.Get(FormPublicationErrors.CatalogInvalid.Code), null, null));
        }
        return issues.AsReadOnly();
    }
}
