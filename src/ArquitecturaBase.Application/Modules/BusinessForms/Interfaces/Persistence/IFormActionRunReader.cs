using ArquitecturaBase.Application.Modules.BusinessForms.Models;
namespace ArquitecturaBase.Application.Modules.BusinessForms.Interfaces.Persistence;
public interface IFormActionRunReader
{
    Task<IReadOnlyList<Guid>> ListExistingPdfRunIdsAsync(IReadOnlyList<Guid> runIds, CancellationToken cancellationToken);
    Task<IReadOnlyList<Guid>> ListDueIdsAsync(DateTime nowUtc,CancellationToken cancellationToken);
    /// <summary>The issuance of a response that waits for this person, its author, to confirm it; null otherwise.</summary>
    Task<FormIssueConfirmationResponse?> FindIssueConfirmationAsync(Guid recordId, Guid actorUserId, CancellationToken cancellationToken);
    Task<FormWorkflowResponse?> FindWorkflowAsync(Guid recordId,CancellationToken cancellationToken);
}
