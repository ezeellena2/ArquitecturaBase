using ArquitecturaBase.Application.Common.Pagination;
using ArquitecturaBase.Application.Modules.BusinessForms.Models;
namespace ArquitecturaBase.Application.Modules.BusinessForms.Interfaces.Persistence;

public interface IFormApprovalReader
{
    Task<FormApprovalActorRow?> FindActorAsync(Guid userId, Guid? roleId, CancellationToken cancellationToken);
    Task<PagedResult<FormApprovalTaskResponse>> ListAsync(Guid actor, ListFormApprovalsRequest request, string culture, CancellationToken cancellationToken);
    Task<int> CountAsync(Guid actor, FormApprovalFilterRequest request, CancellationToken cancellationToken);
    Task<IReadOnlyList<FormApprovalFormResponse>> ListFormsAsync(Guid actor, bool onlyMine, string culture, CancellationToken cancellationToken);
    Task<FormApprovalTaskResponse?> FindAsync(Guid id, Guid actor, string culture, CancellationToken cancellationToken);
    Task<FormActionRecordSnapshot?> FindActionSnapshotAsync(Guid recordId, CancellationToken cancellationToken);
}
