using ArquitecturaBase.Application.Modules.WhatsApp.Interfaces.Integrations;
using ArquitecturaBase.Application.Interfaces.Persistence;
using ArquitecturaBase.Application.Modules.WhatsApp.Interfaces.Persistence;
using ArquitecturaBase.Application.Modules.WhatsApp.Interfaces.Services;
using ArquitecturaBase.Application.Modules.WhatsApp.Models;
using ArquitecturaBase.Domain.Modules.WhatsApp;
using ArquitecturaBase.Domain.Results;

namespace ArquitecturaBase.Application.Modules.WhatsApp.Services;

internal sealed class WhatsAppFlowReplyStateService(IWhatsAppMessageRepository messages,
    IWhatsAppFlowSessionRepository sessions, IUnitOfWork unitOfWork, TimeProvider timeProvider, IWhatsAppInboundSignal inboundSignal,
    WhatsAppFlowRejectionPolicy rejections) : IWhatsAppFlowReplyStateService
{
    public Task<Result> MarkDownloadingAsync(string waMessageId, Guid sessionId, int fileCount, CancellationToken cancellationToken) =>
        unitOfWork.ExecuteInTransactionAsync(async ct =>
        {
            await messages.LockAsync([waMessageId], ct);
            var message = await messages.GetInboundAsync(waMessageId, ct);
            if (message is not null && !message.ProcessedAtUtc.HasValue)
            {
                message.AttachFlowSession(sessionId);
                message.MarkFlowFiles(fileCount);
            }
            return Result.Success();
        }, CommitPolicy.OnSuccess, cancellationToken);

    public Task<Result> RegisterFailureAsync(string waMessageId, Guid? sessionId, CancellationToken cancellationToken, string? failureCode = null, string? rejectedQuestion = null) =>
        unitOfWork.ExecuteInTransactionAsync(async ct =>
        {
            await messages.LockAsync([waMessageId], ct);
            var message = await messages.GetInboundAsync(waMessageId, ct);
            if (message is null || message.ProcessedAtUtc.HasValue) return Result.Success();
            if (sessionId is { } id) message.AttachFlowSession(id);
            if (message.RegisterFlowReplyFailure(timeProvider.GetUtcNow().UtcDateTime))
            {
                message.SetFlowReplyCapture(WhatsAppFlowCaptureState.Rejected, null, null, failureCode ?? FlowErrors.CaptureFailed.Code, rejectedQuestion);
                message.MarkProcessed(timeProvider.GetUtcNow().UtcDateTime);
                if (message.FlowSessionId is { } knownSessionId)
                {
                    var session = await sessions.GetAsync(knownSessionId, ct);
                    session?.CompleteCapture(waMessageId, WhatsAppFlowCaptureState.Rejected, null, null, failureCode ?? FlowErrors.CaptureFailed.Code, rejectedQuestion);
                }
            }
            return Result.Success();
        }, CommitPolicy.OnSuccess, cancellationToken);

    public async Task<Result> MarkProcessedAsync(string waMessageId, Guid? sessionId, WhatsAppFlowCaptureState state,
        Guid? recordId, string? number, string? errorCode, CancellationToken cancellationToken, string? rejectedQuestion = null,
        WhatsAppFlowRejection? rejection = null)
    {
        var result = await unitOfWork.ExecuteInTransactionAsync(async ct =>
        {
            await messages.LockAsync([waMessageId], ct);
            var message = await messages.GetInboundAsync(waMessageId, ct);
            message?.SetFlowReplyCapture(state, recordId, number, errorCode, rejectedQuestion);
            if (message is not null && sessionId is { } id)
            {
                message.AttachFlowSession(id);
                var session = await sessions.GetAsync(id, ct);
                // Task 9c: what the rejection answers (the form again or a notice) is queued with it, before the reply is marked processed.
                if (session?.CompleteCapture(waMessageId, state, recordId, number, errorCode, rejectedQuestion) == true && rejection is not null)
                    await rejections.QueueAsync(message, session, rejection, ct);
            }
            message?.MarkProcessed(timeProvider.GetUtcNow().UtcDateTime);
            return Result.Success();
        }, CommitPolicy.OnSuccess, cancellationToken);
        if (result.IsSuccess && state == WhatsAppFlowCaptureState.Completed && recordId.HasValue)
            inboundSignal.Notify();
        return result;
    }
}
