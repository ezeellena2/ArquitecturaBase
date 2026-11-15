using ArquitecturaBase.Application.Common.Pagination;
using ArquitecturaBase.Application.Modules.BusinessForms.Models;
namespace ArquitecturaBase.Application.Modules.BusinessForms.Interfaces.Persistence;

public interface IFormApprovalReader
{
    /// <summary>The person corrected the response or passed it from a received document: they set its values and do not approve it.</summary>
    Task<bool> ExistsValuesSetByAsync(Guid recordId, Guid userId, CancellationToken cancellationToken);
    Task<FormApprovalActorRow?> FindActorAsync(Guid userId, Guid? roleId, CancellationToken cancellationToken);
    Task<PagedResult<FormApprovalTaskResponse>> ListAsync(Guid actor, ListFormApprovalsRequest request, string culture, CancellationToken cancellationToken);
    Task<int> CountAsync(Guid actor, FormApprovalFilterRequest request, CancellationToken cancellationToken);
    Task<FormApprovalTaskResponse?> FindAsync(Guid id, Guid actor, string culture, CancellationToken cancellationToken);
    Task<FormApprovalContinuationResponse?> FindContinuationAsync(Guid id, Guid actor, FormApprovalContinuationRequest request, string culture, CancellationToken cancellationToken);
    Task<FormActionRecordRow?> FindActionSnapshotAsync(Guid recordId, CancellationToken cancellationToken);
}
