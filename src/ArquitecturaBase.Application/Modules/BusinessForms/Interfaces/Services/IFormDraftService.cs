using ArquitecturaBase.Application.Common.Pagination;
using ArquitecturaBase.Application.Modules.BusinessForms.Models;
using ArquitecturaBase.Domain.Results;
namespace ArquitecturaBase.Application.Modules.BusinessForms.Interfaces.Services;
public interface IFormDraftService
{
    Task<Result<FormDraftCommitResponse>> CreateAsync(SaveFormDraftRequest request, CancellationToken cancellationToken);
    Task<Result<FormDraftCommitResponse>> UpdateAsync(Guid id, SaveFormDraftRequest request, CancellationToken cancellationToken);
    Task<Result<FormDefinitionDetailResponse>> GetAsync(Guid id, CancellationToken cancellationToken);
    Task<Result<FormRecoveredChangeResponse>> GetChangeAsync(Guid requestId, CancellationToken cancellationToken);
    Task<Result<PagedResult<FormDefinitionListResponse>>> ListAsync(ListFormDefinitionsRequest request, CancellationToken cancellationToken);
    Task<Result<FormDefinitionFacetsResponse>> GetFacetsAsync(ListFormDefinitionsRequest request, CancellationToken cancellationToken);
}
