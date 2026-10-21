using System.Security.Cryptography;
using ArquitecturaBase.Application.Interfaces.Integrations.Files;
using ArquitecturaBase.Application.Modules.WhatsApp.Interfaces.Services;
using ArquitecturaBase.Application.Modules.WhatsApp.Models;
using ArquitecturaBase.Domain.Modules.WhatsApp;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace ArquitecturaBase.Infrastructure.Modules.WhatsApp;

internal sealed partial class WhatsAppOutgoingBackgroundService(
    IServiceScopeFactory scopes, IWhatsAppDispatchCoordinator coordinator, IOptions<WhatsAppOptions> options,
    TimeProvider timeProvider, WhatsAppHealth health, ILogger<WhatsAppOutgoingBackgroundService> logger) : BackgroundService
{
    public async Task<bool> ProcessNextAsync(CancellationToken cancellationToken)
    {
        WhatsAppDispatchReservationResponse reservation;
        await using (var scope = scopes.CreateAsyncScope())
        {
            var service = scope.ServiceProvider.GetRequiredService<IWhatsAppChatDispatchService>();
            await service.RecoverExpiredAsync(cancellationToken);
            reservation = (await service.ReserveAsync(cancellationToken)).Value;
        }

        var operation = reservation.Operation;
        if (operation is null) { return reservation.Progressed; }
        string? providerId = null;
        var uncertain = false;
        var providerInvoked = false;
        string? failureCode = null;
        try
        {
            WhatsAppOutboundMessage? message = operation.Kind switch
            {
                WhatsAppOutgoingMessageKind.Flow => null,
                WhatsAppOutgoingMessageKind.QuickReplies => new WhatsAppReplyButtonsMessage(operation.To, operation.Body,
                    (operation.QuickReplies ?? []).Select(reply => new WhatsAppReplyButton(reply.Id, reply.Title)).ToArray()),
                _ => new WhatsAppTextMessage(operation.To, operation.Body),
            };
            if (operation.Origin == WhatsAppMessageOrigin.FormAction && operation.Kind == WhatsAppOutgoingMessageKind.Document)
            {
                message = null;
                var document = operation.Document ?? throw new InvalidOperationException("A document send has no private metadata.");
                var mediaId = operation.DocumentMediaId;
                if (mediaId is null)
                {
                    using var preparationTimeout = new CancellationTokenSource(TimeSpan.FromSeconds(90), timeProvider);
                    using var preparationCancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, preparationTimeout.Token);
                    var preparationToken = preparationCancellation.Token;
                    await using var preparation = scopes.CreateAsyncScope();
                    var store = preparation.ServiceProvider.GetRequiredService<IPrivateFileStore>();
                    await using var content = await store.OpenReadAsync(document.StorageKey, preparationToken);
                    await using var verified = content is null ? null : await ReadDocumentAsync(content, document, preparationToken);
                    mediaId = verified is null ? null : await preparation.ServiceProvider.GetRequiredService<IWhatsAppMediaUploadClient>()
                        .UploadAsync(document, verified, preparationToken);
                    failureCode = verified is null ? WhatsAppFormActionErrors.DocumentUnavailable.Code : WhatsAppFormActionErrors.UploadFailed.Code;
                    if (mediaId is not null)
                    {
                        await using var recording = scopes.CreateAsyncScope();
                        var recorded = await recording.ServiceProvider.GetRequiredService<IWhatsAppChatDispatchService>()
                            .RecordFormActionMediaAsync(operation.Id, operation.DispatchReservationId!.Value, mediaId, cancellationToken);
                        if (recorded.IsFailure || !recorded.Value) return true;
                    }
                }
                if (mediaId is not null) message = new WhatsAppDocumentMessage(operation.To, mediaId, document.Name, operation.Body);
            }
            if (message is not null || operation.Kind == WhatsAppOutgoingMessageKind.Flow)
            {
                await using var lease = await coordinator.AcquireAsync(operation.To, cancellationToken);
                using var sending = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, lease.LostOwnership);
                await using var scope = scopes.CreateAsyncScope();
                if (operation.Kind == WhatsAppOutgoingMessageKind.Flow)
                {
                    if (operation.FlowSessionId is null) throw new InvalidOperationException("A Flow send has no session.");
                    var prepared = await scope.ServiceProvider.GetRequiredService<IWhatsAppFormMessageDispatchService>()
                        .PrepareAsync(operation.Id, sending.Token);
                    if (prepared.IsSuccess) message = prepared.Value;
                    else failureCode = prepared.Error.Code;
                }
                if (message is null) throw new InvalidOperationException("The outgoing message could not be prepared.");
                var dispatch = scope.ServiceProvider.GetRequiredService<IWhatsAppChatDispatchService>();
                if (operation.Origin is WhatsAppMessageOrigin.FormAction or WhatsAppMessageOrigin.Assistant)
                {
                    var started = operation.Origin == WhatsAppMessageOrigin.Assistant
                        ? await dispatch.MarkAssistantMessageEffectAsync(operation.ContactId, operation.Id, operation.DispatchReservationId!.Value, sending.Token)
                        : await dispatch.MarkFormActionMessageEffectAsync(operation.ContactId, operation.Id, operation.DispatchReservationId!.Value, sending.Token);
                    if (started.IsFailure || !started.Value) return true;
                }
                var client = scope.ServiceProvider.GetRequiredService<IWhatsAppCloudClient>();
                providerInvoked = true;
                var result = await client.SendAsync(message, sending.Token);
                providerId = result.WaMessageId;
                if (!result.IsSent && operation.Origin == WhatsAppMessageOrigin.FormAction)
                    failureCode = result.Failure == WhatsAppSendFailure.OutsideCustomerServiceWindow
                        ? WhatsAppFormActionErrors.WindowClosed.Code : WhatsAppFormActionErrors.SendRejected.Code;
                uncertain = result.OutcomeUncertain || result.Failure == WhatsAppSendFailure.Transient;
                if (result.IsSent) { health.ReportSent(); }
                else if (result.Failure is { } failure && failure.IsTokenProblem()) { health.ReportTokenProblem(failure); }
            }
        }
        catch (Exception) when (!cancellationToken.IsCancellationRequested)
        {
            uncertain = providerInvoked;
            failureCode ??= operation.Kind == WhatsAppOutgoingMessageKind.Document ? WhatsAppFormActionErrors.DocumentUnavailable.Code : WhatsAppFormActionErrors.SendRejected.Code;
            if (providerInvoked) LogDispatchUnknown(logger);
            else LogDispatchFailedBeforeProvider(logger);
        }

        // A known system outcome is confirmed with a bounded token independent of shutdown. Interrupted effects without an id are recovered as Uncertain.
        using var completionTimeout = new CancellationTokenSource(TimeSpan.FromSeconds(10), timeProvider);
        var completionToken = operation.Origin is WhatsAppMessageOrigin.FormAction or WhatsAppMessageOrigin.Assistant ? completionTimeout.Token : cancellationToken;
        await using var completion = scopes.CreateAsyncScope();
        await completion.ServiceProvider.GetRequiredService<IWhatsAppChatDispatchService>()
            .CompleteAsync(operation.ContactId, operation.Id, providerId, uncertain, completionToken, operation.DispatchReservationId, failureCode);
        return true;
    }

    private static async Task<MemoryStream?> ReadDocumentAsync(Stream content, WhatsAppOutgoingDocument document, CancellationToken cancellationToken)
    {
        var result = new MemoryStream();
        var buffer = new byte[64 * 1024];
        while (true)
        {
            var read = await content.ReadAsync(buffer, cancellationToken);
            if (read == 0) break;
            if (result.Length + read > document.Size) { await result.DisposeAsync(); return null; }
            await result.WriteAsync(buffer.AsMemory(0, read), cancellationToken);
        }
        if (result.Length != document.Size || !Convert.ToHexString(SHA256.HashData(result.GetBuffer().AsSpan(0, (int)result.Length)))
            .Equals(document.Sha256, StringComparison.OrdinalIgnoreCase))
        { await result.DisposeAsync(); return null; }
        result.Position = 0;
        return result;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!options.Value.ProcessOutgoingInBackground) { return; }
        while (!stoppingToken.IsCancellationRequested)
        {
            try { if (await ProcessNextAsync(stoppingToken)) continue; }
            catch (Exception) when (!stoppingToken.IsCancellationRequested) { LogDispatchUnknown(logger); }
            await Task.Delay(TimeSpan.FromSeconds(2), timeProvider, stoppingToken);
        }
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "The WhatsApp outgoing dispatch failed before invoking the provider.")]
    private static partial void LogDispatchFailedBeforeProvider(ILogger logger);

    [LoggerMessage(Level = LogLevel.Warning, Message = "The manual WhatsApp dispatch could not confirm its outcome; it is not repeated automatically.")]
    private static partial void LogDispatchUnknown(ILogger logger);
}
