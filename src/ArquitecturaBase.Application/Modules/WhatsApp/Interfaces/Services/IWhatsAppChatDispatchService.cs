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

    /// <summary>
    /// H8: a reserved send that stopped before Meta was called (the host stopped while it waited for its turn to write to that
    /// person) goes back to Pending in its place; one whose message effect already started stays and is recovered as uncertain.
    /// H3: with <paramref name="wait"/> (the turn could not be taken, as with Redis down) it waits that long before its next
    /// attempt, and meanwhile nothing else of its chat goes out.
    /// </summary>
    Task<Result> ReturnUnsentAsync(Guid contactId, Guid operationId, Guid? reservationId, TimeSpan? wait, CancellationToken cancellationToken);

    /// <summary>
    /// H7: a send that did not go out (the network or Meta refused it before it arrived, and it is worth retrying) goes back to
    /// Pending in its place and waits <see cref="Models.WhatsAppDispatchPacing.RetryWait"/>; meanwhile nothing else of its chat goes out.
    /// </summary>
    Task<Result> RetryLaterAsync(Guid contactId, Guid operationId, Guid? reservationId, CancellationToken cancellationToken);
}
