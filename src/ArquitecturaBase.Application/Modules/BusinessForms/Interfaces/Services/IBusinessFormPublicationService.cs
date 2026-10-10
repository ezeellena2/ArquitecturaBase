using ArquitecturaBase.Application.Modules.BusinessForms.Models;
using ArquitecturaBase.Domain.Results;
namespace ArquitecturaBase.Application.Modules.BusinessForms.Interfaces.Services;
public interface IBusinessFormPublicationService
{
    Task<Result<FormPublicationCheckResponse>> CheckAsync(Guid id, CheckFormPublicationRequest request, CancellationToken cancellationToken);
}