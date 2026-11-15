using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace ArquitecturaBase.Infrastructure.Modules.Arca.Invoicing;

/// <summary>
/// Recorre la cola de emisión; lo que hace con cada secuencia es de <see cref="ArcaVoucherProcessor"/>. Espera
/// <c>PollInterval</c> entre vueltas, salvo después de una que pidió un CAE o guardó un PDF, porque puede haber más en cola (H33).
/// </summary>
internal sealed partial class ArcaVoucherBackgroundService(ArcaVoucherProcessor processor, IOptions<ArcaVoucherWorkerOptions> options,
    TimeProvider timeProvider, ILogger<ArcaVoucherBackgroundService> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!options.Value.ProcessInBackground) return;
        var observedAt = DateTimeOffset.MinValue;
        while (!stoppingToken.IsCancellationRequested)
        {
            var worked = false;
            try
            {
                worked = await processor.ProcessDueAsync(stoppingToken);
                worked |= await processor.ProcessDuePdfsAsync(stoppingToken);
                // H26: la métrica de la cola, como mucho una vez por intervalo aunque haya trabajo seguido.
                if (timeProvider.GetUtcNow() - observedAt >= options.Value.PollInterval)
                {
                    await processor.ObserveAsync(stoppingToken);
                    observedAt = timeProvider.GetUtcNow();
                }
            }
            catch (Exception exception) when (!stoppingToken.IsCancellationRequested)
            {
                LogFailed(exception.GetType().Name);
            }

            if (!worked) await Task.Delay(options.Value.PollInterval, timeProvider, stoppingToken);
        }
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "The ARCA voucher queue failed with {ExceptionType}; it runs again on the next interval.")]
    private partial void LogFailed(string exceptionType);
}
