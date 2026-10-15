using ArquitecturaBase.Domain.Modules.WhatsApp;
using ArquitecturaBase.Domain.Results;

namespace ArquitecturaBase.Application.Modules.WhatsApp.Interfaces.Services;

/// <summary>Persists a processed reply and its session capture state in one transaction.</summary>
public interface IWhatsAppFlowReplyStateService
{
    Task<Result> MarkDownloadingAsync(string waMessageId, Guid sessionId, int fileCount, CancellationToken cancellationToken);
    Task<Result> RegisterFailureAsync(string waMessageId, Guid? sessionId, CancellationToken cancellationToken, string? failureCode = null, string? rejectedQuestion = null);
    Task<Result> MarkProcessedAsync(string waMessageId, Guid? sessionId, WhatsAppFlowCaptureState state,
        Guid? recordId, string? number, string? errorCode, CancellationToken cancellationToken, string? rejectedQuestion = null);
}
