using ArquitecturaBase.Application.Common.Pagination;
using ArquitecturaBase.Application.Modules.BusinessForms.Models;

namespace ArquitecturaBase.Application.Modules.BusinessForms.Interfaces.Persistence;

public interface IFormCatalogReader
{
    Task<FormCatalogStoredRow?> FindAsync(Guid id, CancellationToken cancellationToken);
    Task<PagedResult<FormCatalogItemResponse>> ListItemsAsync(Guid id, int page, int pageSize, CancellationToken cancellationToken);
}
