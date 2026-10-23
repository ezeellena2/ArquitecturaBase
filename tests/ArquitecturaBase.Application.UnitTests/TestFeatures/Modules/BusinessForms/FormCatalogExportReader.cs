using ArquitecturaBase.Application.Common.Pagination;
using ArquitecturaBase.Application.Modules.BusinessForms.Interfaces.Persistence;
using ArquitecturaBase.Application.Modules.BusinessForms.Models;

namespace ArquitecturaBase.Application.UnitTests.TestFeatures.Modules.BusinessForms;

internal sealed class FormCatalogExportReader : IFormCatalogReader
{
    public FormCatalogStoredRow? Catalog { get; set; } = new(Guid.NewGuid(), "catalog-test", "{}", "{}", "Manual", 3,
        true, default, null, null, null, 0);
    public IReadOnlyList<FormCatalogStoredItemRow> Items { get; set; } = [];
    public int DefinitionReads { get; private set; }
    public int AllItemsReads { get; private set; }
    public Action? AfterDefinition { get; set; }
    public Action? AfterItems { get; set; }
    public Task<FormCatalogStoredRow?> FindAsync(Guid id, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested(); DefinitionReads++; AfterDefinition?.Invoke();
        return Task.FromResult(Catalog);
    }
    public Task<IReadOnlyList<FormCatalogStoredItemRow>> ListAllItemsAsync(Guid id, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested(); AllItemsReads++; AfterItems?.Invoke();
        return Task.FromResult(Items);
    }
    public Task<IReadOnlyList<FormCatalogStoredItemRow>> ListOfferItemsAsync(Guid id, CancellationToken ct, string? fieldValueContainmentJson = null) => throw new NotSupportedException();
    public Task<int> CountActiveItemsAsync(Guid catalogId, CancellationToken cancellationToken) => throw new NotSupportedException();
    public Task<int> CountUsageAsync(Guid id, CancellationToken ct) => throw new NotSupportedException();
    public Task<FormCatalogsPageResponse> FindPageAsync(ListFormCatalogsRequest request, bool canEdit, bool canReadForms, CancellationToken ct) => throw new NotSupportedException();
    public Task<PagedResult<FormCatalogItemResponse>> ListItemsAsync(Guid id, ListFormCatalogItemsRequest request, CancellationToken ct) => throw new NotSupportedException();
}
