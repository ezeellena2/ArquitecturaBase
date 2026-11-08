using ArquitecturaBase.Application.Common.Pagination;
using ArquitecturaBase.Application.Modules.BusinessForms.Models;

namespace ArquitecturaBase.Application.Modules.BusinessForms.Interfaces.Persistence;

public interface IFormCatalogReader
{
    Task<IReadOnlyList<FormCatalogStoredItemRow>> ListAllItemsAsync(Guid catalogId, CancellationToken cancellationToken);
    /// <summary>The active options within the containment, in order, up to <paramref name="limit"/> (one more than a list takes, to tell it passed).</summary>
    Task<IReadOnlyList<FormCatalogStoredItemRow>> ListOfferItemsAsync(Guid catalogId, CancellationToken cancellationToken,
        string? fieldValueContainmentJson = null, int limit = 201);
    Task<FormCatalogsPageResponse> FindPageAsync(ListFormCatalogsRequest request, bool canEdit, bool canReadForms,
        CancellationToken cancellationToken);
    Task<FormCatalogStoredRow?> FindAsync(Guid id, CancellationToken cancellationToken);
    Task<int> CountUsageAsync(Guid catalogId, CancellationToken cancellationToken);
    Task<IReadOnlyList<FormCatalogFieldUsageRow>> ListFieldUsageAsync(Guid catalogId, IReadOnlyCollection<Guid> fieldIds, CancellationToken cancellationToken);
    Task<bool> ExistsSourceUsageAsync(Guid catalogId, CancellationToken cancellationToken);
    Task<IReadOnlyList<FormCatalogItemUsageRow>> ListItemUsesAsync(Guid catalogId, IReadOnlyCollection<Guid> itemIds, CancellationToken cancellationToken);
    // LD1-A: with the field a question chooses first by, it also counts the values and the options of the largest one.
    Task<FormCatalogMaximumOfferRow> FindMaximumOfferSizeAsync(Guid catalogId, string? fixedContainmentJson, IReadOnlyCollection<Guid> personFieldIds,
        CancellationToken cancellationToken, Guid? groupByFieldId = null);
    Task<int> CountActiveItemsAsync(Guid catalogId, CancellationToken cancellationToken);
    Task<PagedResult<FormCatalogItemResponse>> ListItemsAsync(Guid id, ListFormCatalogItemsRequest request, CancellationToken cancellationToken);
}
