using ArquitecturaBase.Application.Common.Pagination;
using ArquitecturaBase.Application.Modules.BusinessForms.Models;

namespace ArquitecturaBase.Application.Modules.BusinessForms.Interfaces.Persistence;

public interface IFormCatalogReader
{
    Task<IReadOnlyList<FormCatalogStoredItemRow>> ListAllItemsAsync(Guid catalogId, CancellationToken cancellationToken);
    Task<IReadOnlyList<FormCatalogStoredItemRow>> ListOfferItemsAsync(Guid catalogId, CancellationToken cancellationToken,
        string? fieldValueContainmentJson = null);
    Task<FormCatalogsPageResponse> FindPageAsync(ListFormCatalogsRequest request, bool canEdit, bool canReadForms,
        CancellationToken cancellationToken);
    Task<FormCatalogStoredRow?> FindAsync(Guid id, CancellationToken cancellationToken);
    Task<int> CountUsageAsync(Guid catalogId, CancellationToken cancellationToken);
    Task<IReadOnlyList<FormCatalogFieldUsageRow>> ListFieldUsageAsync(Guid catalogId, IReadOnlyCollection<Guid> fieldIds, CancellationToken cancellationToken);
    Task<bool> ExistsSourceUsageAsync(Guid catalogId, CancellationToken cancellationToken);
    Task<FormCatalogMaximumOfferRow> FindMaximumOfferSizeAsync(Guid catalogId, string? fixedContainmentJson, IReadOnlyCollection<Guid> personFieldIds, CancellationToken cancellationToken);
    Task<int> CountActiveItemsAsync(Guid catalogId, CancellationToken cancellationToken);
    Task<PagedResult<FormCatalogItemResponse>> ListItemsAsync(Guid id, ListFormCatalogItemsRequest request, CancellationToken cancellationToken);
}
