using ArquitecturaBase.Application.Modules.BusinessForms.Models;
using ArquitecturaBase.Domain.Results;

namespace ArquitecturaBase.Application.Modules.BusinessForms.Interfaces.Services;

public interface IFormPublicationChannelDispatchService
{
    Task<Result<FormPublicationChannelDispatchResponse>> DispatchAsync(FormPublicationWorkResponse work,
        CancellationToken cancellationToken);
}
