using ArquitecturaBase.Application.Interfaces.Persistence;
using ArquitecturaBase.Application.Modules.WhatsApp.Interfaces.Persistence;
using ArquitecturaBase.Application.Modules.WhatsApp.Interfaces.Services;
using ArquitecturaBase.Domain.Modules.WhatsApp;
using ArquitecturaBase.Domain.Results;

namespace ArquitecturaBase.Application.Modules.WhatsApp.Services;

internal sealed class WhatsAppFlowReplyStateService(IWhatsAppMessageRepository messages,
    IWhatsAppFlowSessionRepository sessions, IUnitOfWork unitOfWork, TimeProvider timeProvider) : IWhatsAppFlowReplyStateService
{
    public Task<Result> MarkProcessedAsync(string waMessageId, Guid? sessionId, WhatsAppFlowCaptureState state,
        Guid? recordId, string? number, string? errorCode, CancellationToken cancellationToken, string? rejectedQuestion = null) =>
        unitOfWork.ExecuteInTransactionAsync(async ct =>
        {
            await messages.LockAsync([waMessageId], ct);
            var message = await messages.GetInboundAsync(waMessageId, ct);
            message?.SetFlowReplyCapture(state, recordId, number, errorCode, rejectedQuestion);
            if (message is not null && sessionId is { } id)
            {
                message.AttachFlowSession(id);
                var session = await sessions.GetAsync(id, ct);
                session?.CompleteCapture(waMessageId, state, recordId, number, errorCode, rejectedQuestion);
            }
            message?.MarkProcessed(timeProvider.GetUtcNow().UtcDateTime);
            return Result.Success();
        }, CommitPolicy.OnSuccess, cancellationToken);
}
