using ArquitecturaBase.Application.Common.Exceptions;
using ArquitecturaBase.Application.Interfaces.Integrations;
using ArquitecturaBase.Application.Interfaces.Services;
using Microsoft.Extensions.Logging;

namespace ArquitecturaBase.Application.Services.WhatsApp;

internal sealed partial class WhatsAppWebhookService(
    IWhatsAppSignatureValidator validator,
    IWhatsAppWebhookReader reader,
    IWhatsAppWebhookPersistence persistence,
    IWhatsAppWebhookRetry retry,
    IWhatsAppInboundSignal inboundSignal,
    ILogger<WhatsAppWebhookService> logger) : IWhatsAppWebhookService
{
    public bool IsValidVerifyToken(string? token) => validator.IsValidVerifyToken(token);

    public async Task<bool> ReceiveAsync(
        ReadOnlyMemory<byte> body, string? signature, CancellationToken cancellationToken)
    {
        if (!validator.IsValidSignature(body.Span, signature))
        {
            LogSignatureRejected(logger, body.Length);
            return false;
        }

        LogHandling(logger);
        var batch = reader.Read(body);

        try
        {
            await persistence.PersistAsync(batch, cancellationToken);
        }
        catch (UniqueConstraintViolationException)
        {
            // The first unit of work rolled back. Re-read in another scope so a concurrent
            // duplicate is skipped while other messages in the batch can still be saved.
            LogConcurrentDuplicate(logger);
            await retry.RetryAsync(batch, cancellationToken);
        }

        // Only signal after the transaction commits. The worker also polls pending messages.
        if (batch.Messages.Count > 0)
        {
            inboundSignal.Notify();
        }

        LogHandled(logger);
        return true;
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Handling ReceiveWhatsAppWebhookCommand")]
    private static partial void LogHandling(ILogger logger);

    [LoggerMessage(Level = LogLevel.Information, Message = "Handled ReceiveWhatsAppWebhookCommand")]
    private static partial void LogHandled(ILogger logger);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Rejected a WhatsApp webhook of {Length} bytes with a missing or invalid signature")]
    private static partial void LogSignatureRejected(ILogger logger, int length);

    [LoggerMessage(Level = LogLevel.Warning,
        Message = "Another request saved part of a WhatsApp webhook at the same time; handling it again, skipping what is already saved")]
    private static partial void LogConcurrentDuplicate(ILogger logger);
}
