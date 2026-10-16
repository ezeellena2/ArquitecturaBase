using ArquitecturaBase.Application.Modules.BusinessForms.Models;
namespace ArquitecturaBase.Application.Modules.BusinessForms.Interfaces.Persistence;
public interface IFormActionRunReader
{
    Task<IReadOnlyList<Guid>> ListDueIdsAsync(DateTime nowUtc,CancellationToken cancellationToken);
    Task<FormWorkflowResponse?> FindWorkflowAsync(Guid recordId,CancellationToken cancellationToken);
}
