using ArquitecturaBase.Application.Interfaces.Persistence;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace ArquitecturaBase.Infrastructure.WhatsApp;

/// <summary>
/// La retención de los mensajes de WhatsApp (sección 6.5 del spec y "Cuánto tiempo los guardamos" de la política de
/// privacidad): a los mensajes, entrantes y salientes, de más de <c>WhatsApp:MessageRetentionDays</c> días les borra el
/// texto. La fila queda, con su fecha, su dirección, su tipo, su estado, el botón que se tocó y cuándo lo procesó el bot,
/// que es el registro que la política dice que se conserva; los contactos no se tocan. Corre al arrancar y después una vez
/// por día, esté WhatsApp prendido o no. Un error se registra y se reintenta en la corrida siguiente: nunca apaga el host.
/// </summary>
internal sealed partial class WhatsAppMessageRetentionService(
    IServiceScopeFactory scopeFactory,
    IOptions<WhatsAppMessageRetentionOptions> options,
    TimeProvider timeProvider,
    ILogger<WhatsAppMessageRetentionService> logger)
    : BackgroundService
{
    /// <summary>Cada cuánto corre, después de la primera vez, que es al arrancar.</summary>
    public static readonly TimeSpan Interval = TimeSpan.FromDays(1);

    /// <summary>
    /// Una corrida: vacía el texto de los mensajes vencidos y devuelve cuántos vació. Deja afuera los que ya no tienen
    /// texto, así que correrla otra vez no cambia nada, y dos instancias a la vez tampoco. La usan el ciclo en segundo
    /// plano y los tests.
    /// </summary>
    public async Task<int> ClearExpiredTextsAsync(CancellationToken cancellationToken = default)
    {
        var retentionDays = options.Value.MessageRetentionDays;
        var cutoffUtc = timeProvider.GetUtcNow().UtcDateTime.AddDays(-retentionDays);

        await using var scope = scopeFactory.CreateAsyncScope();
        var repository = scope.ServiceProvider.GetRequiredService<IWhatsAppMessageRetentionRepository>();
        var cleared = await repository.ClearExpiredBodiesAsync(cutoffUtc, cancellationToken);

        LogCleared(logger, cleared, retentionDays);

        return cleared;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        // Apagada, los textos esperan a que alguien llame a ClearExpiredTextsAsync: así la usan los tests.
        if (!options.Value.ApplyMessageRetentionInBackground)
        {
            return;
        }

        // El timer arranca antes de la primera corrida: la siguiente es un día después del arranque, dure lo que dure esta.
        using var timer = new PeriodicTimer(Interval, timeProvider);

        do
        {
            try
            {
                await ClearExpiredTextsAsync(stoppingToken);
            }
            catch (Exception exception) when (!stoppingToken.IsCancellationRequested)
            {
                // Por ejemplo, la base no respondió. Un error que saliera de acá apagaría el host entero: se registra y
                // la corrida de mañana lo intenta de nuevo.
                LogRunFailed(logger, exception);
            }
        }
        while (await timer.WaitForNextTickAsync(stoppingToken));
    }

    // Solo cuántos y con qué plazo: nunca un texto, un número ni un id de mensaje.
    [LoggerMessage(
        Level = LogLevel.Information,
        Message = "The WhatsApp message retention cleared the text of {Count} messages older than {RetentionDays} days")]
    private static partial void LogCleared(ILogger logger, int count, int retentionDays);

    [LoggerMessage(
        Level = LogLevel.Error,
        Message = "The WhatsApp message retention failed; it runs again in the next round")]
    private static partial void LogRunFailed(ILogger logger, Exception exception);
}
