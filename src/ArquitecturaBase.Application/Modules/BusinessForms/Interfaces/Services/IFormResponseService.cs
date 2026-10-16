using ArquitecturaBase.Application.Common.Pagination;
using ArquitecturaBase.Application.Modules.BusinessForms.Models;
using ArquitecturaBase.Domain.Results;
namespace ArquitecturaBase.Application.Modules.BusinessForms.Interfaces.Services;
public interface IFormResponseService
{
    Task<Result<PagedResult<FormNavigationResponse>>> ListNavigationAsync(FormNavigationRequest request, CancellationToken cancellationToken);
    Task<Result<FormResponseSheetResponse>> ListAsync(Guid id, ListFormRecordsRequest request, CancellationToken cancellationToken);
}
