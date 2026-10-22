using ArquitecturaBase.Application.Common.Pagination;
using ArquitecturaBase.Application.Modules.BusinessForms.Models;

namespace ArquitecturaBase.Application.Modules.BusinessForms.Interfaces.Persistence;

public interface IFormCatalogReader
{
    Task<IReadOnlyList<FormCatalogStoredItemRow>> ListAllItemsAsync(Guid catalogId, CancellationToken cancellationToken);
    Task<IReadOnlyList<FormCatalogStoredItemRow>> ListOfferItemsAsync(Guid catalogId, CancellationToken cancellationToken);
    Task<FormCatalogsPageResponse> FindPageAsync(ListFormCatalogsRequest request, bool canEdit, bool canReadForms,
        CancellationToken cancellationToken);
    Task<FormCatalogStoredRow?> FindAsync(Guid id, CancellationToken cancellationToken);
    Task<int> CountUsageAsync(Guid catalogId, CancellationToken cancellationToken);
    Task<PagedResult<FormCatalogItemResponse>> ListItemsAsync(Guid id, ListFormCatalogItemsRequest request, CancellationToken cancellationToken);
}
