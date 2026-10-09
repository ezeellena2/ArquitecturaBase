using ArquitecturaBase.Application.Modules.ArtificialIntelligence.Interfaces.Services;
using ArquitecturaBase.Application.Modules.ArtificialIntelligence.Interfaces.Persistence;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace ArquitecturaBase.Infrastructure.Modules.ArtificialIntelligence.Workers;

internal sealed partial class AiAssistantRetentionBackgroundService(
    IServiceScopeFactory scopes, TimeProvider timeProvider, IOptions<AiAssistantOptions> options,
    AiAssistantTelemetry telemetry, ILogger<AiAssistantRetentionBackgroundService> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!options.Value.ProcessInBackground) return;
        var nextPurgeAtUtc = timeProvider.GetUtcNow().UtcDateTime;
        var nextObservationAtUtc = nextPurgeAtUtc;
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await using (var scope = scopes.CreateAsyncScope())
                {
                    var reconciled = await scope.ServiceProvider.GetRequiredService<IAiAssistantRetentionService>().ReconcileExpiredBatchAsync(stoppingToken);
                    if (reconciled.IsFailure)
                    {
                        telemetry.RecordMaintenanceFailure();
                        LogMaintenanceRejected(logger, "reconcile", reconciled.Error.Code);
                    }
                }
                var nowUtc = timeProvider.GetUtcNow().UtcDateTime;
                if (nowUtc >= nextPurgeAtUtc)
                {
                    await PurgeDueContentAsync(stoppingToken);
                    nextPurgeAtUtc = nowUtc.AddMinutes(options.Value.MaintenanceIntervalMinutes);
                }
                if (nowUtc >= nextObservationAtUtc)
                {
                    await using var scope = scopes.CreateAsyncScope();
                    telemetry.Observe(await scope.ServiceProvider.GetRequiredService<IAiAssistantReader>()
                        .FindWorkloadAsync(nowUtc, options.Value.Limits.LeaseGraceSeconds, stoppingToken));
                    nextObservationAtUtc = nowUtc.AddSeconds(30);
                }
            }
            catch (Exception exception) when (!stoppingToken.IsCancellationRequested)
            {
                telemetry.RecordMaintenanceFailure();
                LogMaintenanceFailed(logger, exception.GetType().Name);
            }
            try { await Task.Delay(TimeSpan.FromSeconds(options.Value.WorkerPollingSeconds), timeProvider, stoppingToken); }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { return; }
        }
    }

    private async Task PurgeDueContentAsync(CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            await using var scope = scopes.CreateAsyncScope();
            var purged = await scope.ServiceProvider.GetRequiredService<IAiAssistantRetentionService>().PurgeBatchAsync(cancellationToken);
            if (purged.IsFailure)
            {
                telemetry.RecordMaintenanceFailure();
                LogMaintenanceRejected(logger, "purge", purged.Error.Code);
                return;
            }
            if (purged.Value < options.Value.Limits.MaintenanceBatchSize) return;
        }
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "Assistant maintenance failed with {ExceptionType}; durable content remains scheduled for another pass.")]
    private static partial void LogMaintenanceFailed(ILogger logger, string exceptionType);
    [LoggerMessage(Level = LogLevel.Warning, Message = "Assistant maintenance unit {Unit} was rejected with {Code}.")]
    private static partial void LogMaintenanceRejected(ILogger logger, string unit, string code);
}
