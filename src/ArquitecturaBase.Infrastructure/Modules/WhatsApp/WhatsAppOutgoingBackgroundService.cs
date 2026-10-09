using ArquitecturaBase.Application.Modules.WhatsApp.Interfaces.Services;
using ArquitecturaBase.Application.Modules.WhatsApp.Models;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace ArquitecturaBase.Infrastructure.Modules.WhatsApp;

internal sealed partial class WhatsAppOutgoingBackgroundService(
    IServiceScopeFactory scopes, IWhatsAppDispatchCoordinator coordinator, IOptions<WhatsAppOptions> options,
    TimeProvider timeProvider, WhatsAppHealth health, ILogger<WhatsAppOutgoingBackgroundService> logger) : BackgroundService
{
    public async Task ProcessNextAsync(CancellationToken cancellationToken)
    {
        WhatsAppDispatchResponse? operation;
        await using (var scope = scopes.CreateAsyncScope())
        {
            var service = scope.ServiceProvider.GetRequiredService<IWhatsAppChatDispatchService>();
            await service.RecoverExpiredAsync(cancellationToken);
            operation = (await service.ReserveAsync(cancellationToken)).Value;
        }

        if (operation is null) { return; }
        string? providerId = null;
        var uncertain = false;
        try
        {
            await using var lease = await coordinator.AcquireAsync(operation.To, cancellationToken);
            using var sending = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, lease.LostOwnership);
            await using var scope = scopes.CreateAsyncScope();
            var result = await scope.ServiceProvider.GetRequiredService<IWhatsAppCloudClient>()
                .SendAsync(new WhatsAppTextMessage(operation.To, operation.Body), sending.Token);
            providerId = result.WaMessageId;
            uncertain = result.OutcomeUncertain || result.Failure == WhatsAppSendFailure.Transient;
            if (result.IsSent) { health.ReportSent(); }
            else if (result.Failure is { } failure && failure.IsTokenProblem()) { health.ReportTokenProblem(failure); }
        }
        catch (Exception) when (!cancellationToken.IsCancellationRequested)
        {
            uncertain = true;
            LogDispatchUnknown(logger);
        }

        // On shutdown a reserved operation remains Sending; recovery makes it Uncertain, without resending.
        await using var completion = scopes.CreateAsyncScope();
        await completion.ServiceProvider.GetRequiredService<IWhatsAppChatDispatchService>()
            .CompleteAsync(operation.ContactId, operation.Id, providerId, uncertain, cancellationToken);
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!options.Value.ProcessOutgoingInBackground) { return; }
        while (!stoppingToken.IsCancellationRequested)
        {
            try { await ProcessNextAsync(stoppingToken); }
            catch (Exception) when (!stoppingToken.IsCancellationRequested) { LogDispatchUnknown(logger); }
            await Task.Delay(TimeSpan.FromSeconds(2), timeProvider, stoppingToken);
        }
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "The manual WhatsApp dispatch could not confirm its outcome; it is not repeated automatically.")]
    private static partial void LogDispatchUnknown(ILogger logger);
}
