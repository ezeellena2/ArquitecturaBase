using ArquitecturaBase.Application.Modules.BusinessForms.Models;
using ArquitecturaBase.Domain.Results;

namespace ArquitecturaBase.Application.Modules.BusinessForms.Interfaces.Services;

public interface IFormPublicationDiscardService
{
    Task<Result> DiscardAsync(Guid id, Guid requestId, DiscardFormPublicationRequest request, CancellationToken cancellationToken);
}
