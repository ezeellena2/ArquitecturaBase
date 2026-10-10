using ArquitecturaBase.Application.Modules.BusinessForms.Models;
using ArquitecturaBase.Domain.Results;

namespace ArquitecturaBase.Application.Modules.BusinessForms.Interfaces.Services;

public interface IFormPublicationAcceptanceService
{
    Task<Result<FormPublicationReceiptResponse>> AcceptAsync(Guid id, AcceptFormPublicationRequest request, CancellationToken cancellationToken);
}
