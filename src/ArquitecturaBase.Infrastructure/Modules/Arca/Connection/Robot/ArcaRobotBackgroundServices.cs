using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace ArquitecturaBase.Infrastructure.Modules.Arca.Connection.Robot;

/// <summary>Corre de a uno los lados del cliente que llegaron a este host. Con el robot apagado no arranca.</summary>
internal sealed partial class ArcaCompanyRobotBackgroundService(ArcaCompanyRobotQueue queue, ArcaRobotProcessor processor,
    IOptions<ArcaRobotBrowserOptions> options, ILogger<ArcaCompanyRobotBackgroundService> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!options.Value.Enabled || !options.Value.ProcessInBackground) return;
        await foreach (var (job, credentials) in queue.ReadAllAsync(stoppingToken))
        {
            try
            {
                await processor.RunCompanyAsync(job, credentials, stoppingToken);
            }
            catch (Exception exception) when (!stoppingToken.IsCancellationRequested)
            {
                LogFailed(exception.GetType().Name);
            }
        }
    }

    [LoggerMessage(Level = LogLevel.Error, Message = "The ARCA robot company run failed with {ExceptionType}; its lease expires and it reads as interrupted.")]
    private partial void LogFailed(string exceptionType);
}

/// <summary>Mira cada <c>Arca:Robot:PollInterval</c> si hay un lado de la plataforma vencido. Con el robot apagado no arranca.</summary>
internal sealed partial class ArcaPlatformRobotBackgroundService(ArcaRobotProcessor processor, IOptions<ArcaRobotBrowserOptions> options,
    TimeProvider timeProvider, ILogger<ArcaPlatformRobotBackgroundService> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!options.Value.Enabled || !options.Value.ProcessInBackground) return;
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await processor.ProcessDuePlatformAsync(stoppingToken);
            }
            catch (Exception exception) when (!stoppingToken.IsCancellationRequested)
            {
                LogFailed(exception.GetType().Name);
            }

            await Task.Delay(options.Value.PollInterval, timeProvider, stoppingToken);
        }
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "The ARCA robot platform side failed with {ExceptionType}; it runs again on the next interval.")]
    private partial void LogFailed(string exceptionType);
}
