using ArquitecturaBase.Application.Modules.Arca.Interfaces.Services.Connection;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace ArquitecturaBase.Infrastructure.Modules.Arca.Connection;

/// <summary>
/// RC7-B: corre el aviso de vencimiento del certificado al arrancar y después cada <c>Arca:CertificateNotice:Interval</c>, cada
/// vez en su scope. Todas las copias lo corren: el lock de la conexión y la marca del certificado hacen que salga una vez. Un
/// error se registra y la vuelta siguiente vuelve a mirar; nunca apaga el host.
/// </summary>
internal sealed partial class ArcaCertificateNoticeBackgroundService(IServiceScopeFactory scopeFactory, IOptions<ArcaCertificateNoticeOptions> options,
    TimeProvider timeProvider, ILogger<ArcaCertificateNoticeBackgroundService> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!options.Value.RunInBackground) return;
        using var timer = new PeriodicTimer(options.Value.Interval, timeProvider);
        try
        {
            do
            {
                try
                {
                    await RunAsync(stoppingToken);
                }
                catch (Exception exception) when (!stoppingToken.IsCancellationRequested)
                {
                    LogRunFailed(exception.GetType().Name);
                }
            }
            while (await timer.WaitForNextTickAsync(stoppingToken));
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
        }
    }

    /// <summary>Una vuelta. La usan el ciclo y los tests.</summary>
    internal async Task RunAsync(CancellationToken cancellationToken)
    {
        await using var scope = scopeFactory.CreateAsyncScope();
        await scope.ServiceProvider.GetRequiredService<IArcaCertificateNoticeService>().NotifyAsync(cancellationToken);
    }

    // Sólo el tipo: el mensaje de una excepción de la base puede traer un valor de la fila.
    [LoggerMessage(Level = LogLevel.Warning, Message = "The ARCA certificate expiry notice failed with {ExceptionType}; it runs again on the next interval.")]
    private partial void LogRunFailed(string exceptionType);
}
