using ArquitecturaBase.Application.Common.Pagination;
using ArquitecturaBase.Application.Modules.BusinessForms.Models;
namespace ArquitecturaBase.Application.Modules.BusinessForms.Interfaces.Persistence;
public interface IFormResponseReader
{
    Task<PagedResult<FormNavigationResponse>> ListNavigationAsync(FormNavigationRequest request, CancellationToken cancellationToken);
    Task<FormResponseDefinitionRow?> FindDefinitionAsync(Guid id, CancellationToken cancellationToken);
    Task<PagedResult<FormResponseStoredRow>> ListAsync(Guid id, ListFormRecordsRequest request, CancellationToken cancellationToken);
}
