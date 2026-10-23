using ArquitecturaBase.Application.Common.Pagination;
using ArquitecturaBase.Application.Modules.BusinessForms.Interfaces.Persistence;
using ArquitecturaBase.Application.Modules.BusinessForms.Models;

namespace ArquitecturaBase.Application.UnitTests.TestFeatures.Modules.BusinessForms;

internal sealed class FormCatalogFieldReader : IFormCatalogReader
{
    public bool SourceInUse { get; set; }
    public List<FormCatalogFieldUsageRow> Uses { get; } = [];
    public Dictionary<Guid, FormCatalogStoredRow> Catalogs { get; } = [];
    public Dictionary<Guid, IReadOnlyList<FormCatalogStoredItemRow>> Items { get; } = [];
    public Task<IReadOnlyList<FormCatalogFieldUsageRow>> FindFieldUsageAsync(Guid catalogId, Guid fieldId, CancellationToken ct) => Task.FromResult<IReadOnlyList<FormCatalogFieldUsageRow>>(Uses);
    public Task<bool> ExistsSourceUsageAsync(Guid catalogId, CancellationToken ct) => Task.FromResult(SourceInUse);
    public Task<IReadOnlyList<FormCatalogStoredItemRow>> ListAllItemsAsync(Guid catalogId, CancellationToken ct) => Task.FromResult(Items.GetValueOrDefault(catalogId) ?? (IReadOnlyList<FormCatalogStoredItemRow>)[]);
    public Task<IReadOnlyList<FormCatalogStoredItemRow>> ListOfferItemsAsync(Guid catalogId, CancellationToken ct, string? fieldValueContainmentJson = null) => throw new NotSupportedException();
    public Task<FormCatalogStoredRow?> FindAsync(Guid id, CancellationToken ct) => Task.FromResult(Catalogs.GetValueOrDefault(id));
    public Task<FormCatalogsPageResponse> FindPageAsync(ListFormCatalogsRequest request, bool canEdit, bool canReadForms, CancellationToken ct) => throw new NotSupportedException();
    public Task<int> CountUsageAsync(Guid catalogId, CancellationToken ct) => Task.FromResult(0);
    public Task<int> CountActiveItemsAsync(Guid catalogId, CancellationToken ct) => throw new NotSupportedException();
    public Task<PagedResult<FormCatalogItemResponse>> ListItemsAsync(Guid id, ListFormCatalogItemsRequest request, CancellationToken ct) => throw new NotSupportedException();
}
