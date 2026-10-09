using ArquitecturaBase.Application.Modules.WhatsApp.Models;
using ArquitecturaBase.Domain.Results;

namespace ArquitecturaBase.Application.Modules.WhatsApp.Interfaces.Services;

public interface IWhatsAppChatDispatchService
{
    Task<Result<WhatsAppDispatchResponse?>> ReserveAsync(CancellationToken cancellationToken);
    Task<Result> CompleteAsync(Guid contactId, Guid operationId, string? waMessageId, bool uncertain, CancellationToken cancellationToken);
    Task<Result> RecoverExpiredAsync(CancellationToken cancellationToken);
}
