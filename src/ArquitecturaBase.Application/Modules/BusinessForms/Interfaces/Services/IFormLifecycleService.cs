using ArquitecturaBase.Application.Modules.BusinessForms.Models;
using ArquitecturaBase.Domain.Results;

namespace ArquitecturaBase.Application.Modules.BusinessForms.Interfaces.Services;

public interface IFormLifecycleService
{
    Task<Result> DeleteAsync(Guid id, DeleteFormRequest request, CancellationToken cancellationToken);
    Task<Result<FormDeactivationPreviewResponse>> GetDeactivationAsync(Guid id, CancellationToken cancellationToken);
    Task<Result<FormDeactivationResponse>> DeactivateAsync(Guid id, DeactivateFormRequest request, CancellationToken cancellationToken);
}
