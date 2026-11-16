using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace ArquitecturaBase.Infrastructure.Modules.ArtificialIntelligence.Workers;

/// <summary>
/// The assistant's lanes. I12: when the host stops (<see cref="IHostApplicationLifetime.ApplicationStopping"/>, before any worker is
/// stopped) no lane takes another turn, and the turns in progress drain for the host's <see cref="HostOptions.ShutdownTimeout"/> less
/// the margin to close them (<see cref="DrainTime"/>); only then is what still runs cut.
/// </summary>
internal sealed partial class AiAssistantBackgroundService(
    AiAssistantCoordinator coordinator, TimeProvider timeProvider, IOptions<AiAssistantOptions> options, IOptions<HostOptions> hostOptions,
    IHostApplicationLifetime lifetime, ILogger<AiAssistantBackgroundService> logger) : BackgroundService
{
    /// <summary>
    /// The drain: the host's shutdown timeout less the margin to close a turn after the cut, its persistence
    /// (<see cref="AiAssistantOptions.CompletionPersistenceSeconds"/>) plus a second to observe it and another to unwind; never below zero.
    /// </summary>
    internal static TimeSpan DrainTime(TimeSpan shutdownTimeout, int completionPersistenceSeconds)
    {
        if (shutdownTimeout == Timeout.InfiniteTimeSpan) return Timeout.InfiniteTimeSpan;
        var drain = shutdownTimeout - TimeSpan.FromSeconds(completionPersistenceSeconds + 2);
        return drain > TimeSpan.Zero ? drain : TimeSpan.Zero;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!options.Value.ProcessInBackground) return;
        using var stopping = CancellationTokenSource.CreateLinkedTokenSource(stoppingToken, lifetime.ApplicationStopping);
        using var drain = new CancellationTokenSource(Timeout.InfiniteTimeSpan, timeProvider);
        var drainTime = DrainTime(hostOptions.Value.ShutdownTimeout, options.Value.CompletionPersistenceSeconds);
        await using var startDrain = stopping.Token.Register(() => drain.CancelAfter(drainTime));
        await Task.WhenAll(Enumerable.Range(0, options.Value.Limits.RunningCapacity).Select(_ => RunLaneAsync(stopping.Token, drain.Token)));
    }

    private async Task RunLaneAsync(CancellationToken stoppingToken, CancellationToken drainToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try { await coordinator.ProcessNextAsync(stoppingToken, drainToken); }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { return; }
            catch (Exception exception)
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
