using ArquitecturaBase.Application.Modules.BusinessForms.Models;
using ArquitecturaBase.Domain.Results;

namespace ArquitecturaBase.Application.Modules.BusinessForms.Interfaces.Services;

public interface IFormPublicationStatusService
{
    Task<Result<FormPublicationStatusResponse>> GetAsync(Guid id, Guid requestId, CancellationToken cancellationToken);
}
