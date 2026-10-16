using ArquitecturaBase.Application.Common.Pagination;
using ArquitecturaBase.Application.Modules.BusinessForms.Models;
using ArquitecturaBase.Domain.Results;
namespace ArquitecturaBase.Application.Modules.BusinessForms.Interfaces.Services;

public interface IFormApprovalService
{
    Task<Result<PagedResult<FormApprovalTaskResponse>>> ListAsync(ListFormApprovalsRequest request, CancellationToken cancellationToken);
    Task<Result<FormApprovalCountResponse>> CountAsync(FormApprovalFilterRequest request, CancellationToken cancellationToken);
    Task<Result<FormApprovalOptionsResponse>> GetOptionsAsync(bool onlyMine, CancellationToken cancellationToken);
    Task<Result<FormApprovalTaskResponse>> GetAsync(Guid id, CancellationToken cancellationToken);
    Task<Result<FormApprovalDecisionResponse>> DecideAsync(Guid id, DecideFormApprovalRequest request, CancellationToken cancellationToken);
}
