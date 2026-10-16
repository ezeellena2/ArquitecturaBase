using ArquitecturaBase.Application.Modules.BusinessForms.Models;
using ArquitecturaBase.Domain.Results;
namespace ArquitecturaBase.Application.Modules.BusinessForms.Interfaces.Services;
public interface IFormWorkflowService
{
    Task<Result<FormWorkflowResponse>> GetAsync(Guid recordId,CancellationToken cancellationToken);
    Task<Result<FormWorkflowResponse>> RetryAsync(Guid recordId,Guid runId,CancellationToken cancellationToken);
    Task<Result<FormWorkflowResponse>> ConsultAsync(Guid recordId,Guid runId,CancellationToken cancellationToken);
}
