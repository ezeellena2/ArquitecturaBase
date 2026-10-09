using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace ArquitecturaBase.Infrastructure.Modules.ArtificialIntelligence.Workers;

internal sealed partial class AiAssistantBackgroundService(
    AiAssistantCoordinator coordinator, TimeProvider timeProvider, IOptions<AiAssistantOptions> options,
    ILogger<AiAssistantBackgroundService> logger) : BackgroundService
{
    protected override Task ExecuteAsync(CancellationToken stoppingToken) => !options.Value.ProcessInBackground
        ? Task.CompletedTask
        : Task.WhenAll(Enumerable.Range(0, options.Value.Limits.RunningCapacity).Select(_ => RunLaneAsync(stoppingToken)));

    private async Task RunLaneAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try { await coordinator.ProcessNextAsync(stoppingToken); }
            catch (Exception exception) when (!stoppingToken.IsCancellationRequested)
            {
                AiAssistantCoordinator.RecordClaimFailure();
                LogClaimFailed(logger, exception.GetType().Name);
            }
            try { await Task.Delay(TimeSpan.FromSeconds(options.Value.WorkerPollingSeconds), timeProvider, stoppingToken); }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { return; }
        }
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "Assistant scheduling failed with {ExceptionType}; the next poll opens a new scope.")]
    private static partial void LogClaimFailed(ILogger logger, string exceptionType);
}
