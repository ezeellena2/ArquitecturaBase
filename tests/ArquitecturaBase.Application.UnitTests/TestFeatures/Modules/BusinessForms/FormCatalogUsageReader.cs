using ArquitecturaBase.Application.Common.Pagination;
using ArquitecturaBase.Application.Modules.BusinessForms.Interfaces.Persistence;
using ArquitecturaBase.Application.Modules.BusinessForms.Models;

namespace ArquitecturaBase.Application.UnitTests.TestFeatures.Modules.BusinessForms;

internal sealed class FormCatalogUsageReader(int usageCount) : IFormCatalogReader
{
    public List<Guid> UsageReads { get; } = [];

    public Task<int> CountActiveItemsAsync(Guid catalogId, CancellationToken cancellationToken) => throw new NotSupportedException();
    public Task<int> CountUsageAsync(Guid catalogId, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        UsageReads.Add(catalogId);
        return Task.FromResult(usageCount);
    }

    public Task<IReadOnlyList<FormCatalogStoredItemRow>> ListAllItemsAsync(Guid id, CancellationToken ct) => throw new NotSupportedException();
    public Task<IReadOnlyList<FormCatalogStoredItemRow>> ListOfferItemsAsync(Guid catalogId, CancellationToken cancellationToken, string? fieldValueContainmentJson = null) =>
        throw new NotSupportedException();
    public Task<FormCatalogsPageResponse> FindPageAsync(ListFormCatalogsRequest request, bool canEdit, bool canReadForms,
        CancellationToken cancellationToken) => throw new NotSupportedException();
    public Task<FormCatalogStoredRow?> FindAsync(Guid id, CancellationToken cancellationToken) => throw new NotSupportedException();
    public Task<PagedResult<FormCatalogItemResponse>> ListItemsAsync(Guid id, ListFormCatalogItemsRequest request,
        CancellationToken cancellationToken) => throw new NotSupportedException();
    public Task<IReadOnlyList<FormCatalogFieldUsageRow>> FindFieldUsageAsync(Guid catalogId, IReadOnlyCollection<Guid> fieldIds, CancellationToken ct) => Task.FromResult<IReadOnlyList<FormCatalogFieldUsageRow>>([]);
    public Task<bool> ExistsSourceUsageAsync(Guid catalogId, CancellationToken ct) => Task.FromResult(false);
}
