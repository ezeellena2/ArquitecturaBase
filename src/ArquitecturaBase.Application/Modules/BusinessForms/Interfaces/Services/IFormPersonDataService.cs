using ArquitecturaBase.Application.Common.Pagination;
using ArquitecturaBase.Application.Modules.BusinessForms.Models;
using ArquitecturaBase.Domain.Results;
namespace ArquitecturaBase.Application.Modules.BusinessForms.Interfaces.Services;
public interface IFormPersonDataService
{
    Task<Result<FormPersonAttributesResponse>> ListAttributesAsync(CancellationToken cancellationToken);
    Task<Result<FormPersonAttributeResponse>> CreateAttributeAsync(CreateFormPersonAttributeRequest request, CancellationToken cancellationToken);
    Task<Result<FormPersonAttributeResponse>> UpdateAttributeAsync(Guid id, UpdateFormPersonAttributeRequest request, CancellationToken cancellationToken);
    Task<Result<PagedResult<FormPersonAttributeOptionResponse>>> ListOptionsAsync(Guid id, ListFormPersonAttributeOptionsRequest request, CancellationToken cancellationToken);
    Task<Result<FormPersonDataPageResponse>> ListPeopleAsync(ListFormPersonDataRequest request, CancellationToken cancellationToken);
    Task<Result<FormPersonDataResponse>> GetDataAsync(Guid userId, CancellationToken cancellationToken);
    Task<Result<FormPersonDataResponse>> SaveDataAsync(Guid userId, UpdateFormPersonDataRequest request, CancellationToken cancellationToken);
}
