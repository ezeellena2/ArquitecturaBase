using ArquitecturaBase.Application.Modules.WhatsApp.Models;
using ArquitecturaBase.Domain.Results;

namespace ArquitecturaBase.Application.Modules.WhatsApp.Interfaces.Services;

public interface IWhatsAppChatDispatchService
{
    Task<Result<WhatsAppDispatchReservationResponse>> ReserveAsync(CancellationToken cancellationToken);
    Task<Result> CompleteAsync(Guid contactId, Guid operationId, string? waMessageId, bool uncertain, CancellationToken cancellationToken, Guid? reservationId = null, string? failureCode = null);
    Task<Result<bool>> MarkAssistantMessageEffectAsync(Guid contactId, Guid operationId, Guid reservationId, CancellationToken cancellationToken);
    Task<Result<bool>> RecordFormActionMediaAsync(Guid operationId, Guid reservationId, string mediaId, CancellationToken cancellationToken);
    Task<Result<bool>> MarkFormActionMessageEffectAsync(Guid contactId, Guid operationId, Guid reservationId, CancellationToken cancellationToken);
    Task<Result> RecoverExpiredAsync(CancellationToken cancellationToken);
}
