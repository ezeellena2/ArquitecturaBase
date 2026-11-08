using ArquitecturaBase.Application.Common.Pagination;
using ArquitecturaBase.Application.Modules.BusinessForms.Interfaces.Persistence;
using ArquitecturaBase.Application.Modules.BusinessForms.Models;

namespace ArquitecturaBase.Application.UnitTests.TestFeatures.Modules.BusinessForms;

internal sealed class FormCatalogExportReader : IFormCatalogReader
{
    public FormCatalogStoredRow? Catalog { get; set; } = new(Guid.NewGuid(), "catalog-test", """{"es":"Catálogo de prueba"}""", "{}", "es", "Manual", 3,
        true, default, null, null, null, 0);
    public IReadOnlyList<FormCatalogStoredItemRow> Items { get; set; } = [];
    // The options of the source catalogs of its fields, by catalog.
    public Dictionary<Guid, IReadOnlyList<FormCatalogStoredItemRow>> SourceItems { get; } = [];
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
        return Task.FromResult(SourceItems.TryGetValue(id, out var source) ? source : Items);
    }
    public Task<IReadOnlyList<FormCatalogStoredItemRow>> ListOfferItemsAsync(Guid id, CancellationToken ct, string? fieldValueContainmentJson = null, int limit = 201) => throw new NotSupportedException();
    public Task<int> CountActiveItemsAsync(Guid catalogId, CancellationToken cancellationToken) => throw new NotSupportedException();
    public Task<int> CountUsageAsync(Guid id, CancellationToken ct) => throw new NotSupportedException();
    public Task<FormCatalogsPageResponse> FindPageAsync(ListFormCatalogsRequest request, bool canEdit, bool canReadForms, CancellationToken ct) => throw new NotSupportedException();
    public Task<PagedResult<FormCatalogItemResponse>> ListItemsAsync(Guid id, ListFormCatalogItemsRequest request, CancellationToken ct) => throw new NotSupportedException();
    public Task<IReadOnlyList<FormCatalogFieldUsageRow>> ListFieldUsageAsync(Guid catalogId, IReadOnlyCollection<Guid> fieldIds, CancellationToken ct) => Task.FromResult<IReadOnlyList<FormCatalogFieldUsageRow>>([]);
    public Task<bool> ExistsSourceUsageAsync(Guid catalogId, CancellationToken ct) => Task.FromResult(false);
    public Task<IReadOnlyList<FormCatalogItemUsageRow>> ListItemUsesAsync(Guid catalogId, IReadOnlyCollection<Guid> itemIds, CancellationToken ct) => throw new NotSupportedException();
    public Task<FormCatalogMaximumOfferRow> FindMaximumOfferSizeAsync(Guid catalogId, string? fixedContainmentJson,
        IReadOnlyCollection<Guid> personFieldIds, CancellationToken cancellationToken, Guid? groupByFieldId = null) => throw new NotSupportedException();
}
