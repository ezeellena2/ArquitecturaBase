using ArquitecturaBase.Application.Configuration.Auth;
using ArquitecturaBase.Application.Interfaces.Services;
using ArquitecturaBase.Domain.Results;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace ArquitecturaBase.Infrastructure.Identity;

/// <summary>
/// N16: corre la retención de los datos del ingreso al arrancar y después cada <c>IdentityRetention:IntervalHours</c>.
/// Una corrida pide lotes, cada uno en su scope y su transacción, mientras alguno sale lleno, y al final la poda de
/// OpenIddict, tramo por tramo, también cada uno en la suya. Todas las copias la corren: la que encuentra tomado el lock
/// de la retención termina su corrida sin esperar, porque otra ya está en eso. Un error se registra y la corrida
/// siguiente sigue desde donde quedó; nunca apaga el host.
/// </summary>
internal sealed partial class IdentityRetentionBackgroundService(
    IServiceScopeFactory scopeFactory,
    IOptions<IdentityRetentionOptions> options,
    TimeProvider timeProvider,
    ILogger<IdentityRetentionBackgroundService> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        // Apagada, la corren los tests cuando quieren.
        if (!options.Value.RunInBackground) return;

        // El timer arranca antes de la primera corrida: la siguiente es un intervalo después del arranque.
        using var timer = new PeriodicTimer(TimeSpan.FromHours(options.Value.IntervalHours), timeProvider);

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
                    LogRunFailed(logger, exception.GetType().Name);
                }
            }
            while (await timer.WaitForNextTickAsync(stoppingToken));
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
        }
    }

    /// <summary>Una corrida entera. La usan el ciclo y los tests.</summary>
    internal async Task RunAsync(CancellationToken cancellationToken)
    {
        // Con otra copia corriendo, el lote devuelve false y el tramo null: la corrida termina.
        while (await InScopeAsync(service => service.PurgeBatchAsync(cancellationToken)) is { IsSuccess: true, Value: true })
        {
        }

        DateTime? from = null;
        do
        {
            var tranche = await InScopeAsync(service => service.PruneSessionsTrancheAsync(from, cancellationToken));
            from = tranche.IsSuccess ? tranche.Value : null;
        }
        while (from is not null);
    }

    // Un lote o un tramo, cada uno en su scope y su transacción.
    private async Task<Result<T>> InScopeAsync<T>(Func<IIdentityRetentionService, Task<Result<T>>> step)
    {
        await using var scope = scopeFactory.CreateAsyncScope();

        return await step(scope.ServiceProvider.GetRequiredService<IIdentityRetentionService>());
    }

    // Solo el tipo: el mensaje de una excepción de la base puede traer un valor de la fila.
    [LoggerMessage(Level = LogLevel.Error, Message = "Identity retention failed with {ExceptionType}; it runs again in the next round")]
    private static partial void LogRunFailed(ILogger logger, string exceptionType);
}
