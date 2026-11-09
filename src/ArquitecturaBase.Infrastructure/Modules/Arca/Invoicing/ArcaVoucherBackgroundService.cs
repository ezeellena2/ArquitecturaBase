using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace ArquitecturaBase.Infrastructure.Modules.Arca.Invoicing;

/// <summary>Recorre la cola de emisión cada <c>PollInterval</c>; lo que hace con cada secuencia es de <see cref="ArcaVoucherProcessor"/>.</summary>
internal sealed partial class ArcaVoucherBackgroundService(ArcaVoucherProcessor processor, IOptions<ArcaVoucherWorkerOptions> options,
    TimeProvider timeProvider, ILogger<ArcaVoucherBackgroundService> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!options.Value.ProcessInBackground) return;
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await processor.ProcessDueAsync(stoppingToken);
                await processor.ProcessDuePdfsAsync(stoppingToken);
            }
            catch (Exception exception) when (!stoppingToken.IsCancellationRequested)
            {
                LogFailed(exception.GetType().Name);
            }

            await Task.Delay(options.Value.PollInterval, timeProvider, stoppingToken);
        }
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "The ARCA voucher queue failed with {ExceptionType}; it runs again on the next interval.")]
    private partial void LogFailed(string exceptionType);
}
