using ArquitecturaBase.Application.Interfaces.Persistence;
using ArquitecturaBase.Application.Interfaces.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace ArquitecturaBase.Infrastructure.WhatsApp;

/// <summary>
/// Contesta los mensajes que guardó el webhook (sección 7 del spec del ingreso con WhatsApp). Se despierta con el aviso
/// del webhook (<see cref="WhatsAppInboundSignal"/>) y además revisa la tabla cada <c>WhatsApp:InboundPollSeconds</c>,
/// así nada se pierde si la app se reinicia o si el mensaje lo recibió otra instancia. Cada contacto corre como un
/// servicio de Application, en su propia transacción: una respuesta para todos sus pendientes, que
/// quedan procesados en la misma transacción. Sirve con varias instancias: el lock de cada contacto no espera, así que
/// dos instancias nunca contestan al mismo contacto a la vez. Un error con un contacto se registra y no frena a los
/// demás ni al host; sus mensajes siguen pendientes para la próxima vuelta.
/// </summary>
internal sealed partial class WhatsAppInboundProcessor(
    WhatsAppInboundSignal signal,
    IServiceScopeFactory scopeFactory,
    IOptions<WhatsAppOptions> options,
    TimeProvider timeProvider,
    ILogger<WhatsAppInboundProcessor> logger)
    : BackgroundService
{
    /// <summary>Cuántos contactos pide por consulta: después de una caída puede haber muchos esperando.</summary>
    private const int BatchSize = 50;

    /// <summary>
    /// Una vuelta: contesta a todos los contactos con mensajes pendientes, del que espera hace más al que espera hace
    /// menos. Cada contacto se intenta una sola vez por vuelta: uno que falla, o que tiene otra instancia, queda para la
    /// siguiente. La usan el ciclo en segundo plano y los tests de integración, que la llaman cuando quieren.
    /// </summary>
    public async Task ProcessPendingAsync(CancellationToken cancellationToken = default)
    {
        var attempted = new HashSet<Guid>();

        while (true)
        {
            var contactIds = await ListPendingContactsAsync(attempted, cancellationToken);

            foreach (var contactId in contactIds)
            {
                attempted.Add(contactId);
                await ProcessContactAsync(contactId, cancellationToken);
            }

            if (contactIds.Count < BatchSize)
            {
                return;
            }
        }
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var settings = options.Value;

        // Apagado, los mensajes esperan a que alguien llame a ProcessPendingAsync: así los usan los tests.
        if (!settings.ProcessInboundInBackground)
        {
            return;
        }

        var interval = TimeSpan.FromSeconds(settings.InboundPollSeconds);

        // La primera vuelta es al arrancar: contesta lo que quedó pendiente de antes de un reinicio.
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await ProcessPendingAsync(stoppingToken);
            }
            catch (Exception exception) when (!stoppingToken.IsCancellationRequested)
            {
                // Por ejemplo, la base no respondió. Un error que saliera de acá apagaría el host entero: se registra y
                // la próxima vuelta lo intenta de nuevo.
                LogRoundFailed(logger, exception);
            }

            await signal.WaitAsync(interval, timeProvider, stoppingToken);
        }
    }

    private async Task<IReadOnlyList<Guid>> ListPendingContactsAsync(
        IReadOnlyCollection<Guid> attempted,
        CancellationToken cancellationToken)
    {
        await using var scope = scopeFactory.CreateAsyncScope();

        return await scope.ServiceProvider.GetRequiredService<IWhatsAppMessageRepository>()
            .ListContactsWithPendingInboundAsync(attempted, BatchSize, cancellationToken);
    }

    /// <summary>
    /// Un scope por contacto: su propio contexto y su propia transacción, que se deshace si algo falla. Así un contacto
    /// con un problema no deja a medio guardar nada de otro.
    /// </summary>
    private async Task ProcessContactAsync(Guid contactId, CancellationToken cancellationToken)
    {
        try
        {
            await using var scope = scopeFactory.CreateAsyncScope();
            var service = scope.ServiceProvider.GetRequiredService<IWhatsAppInboundService>();

            await service.ProcessContactAsync(contactId, cancellationToken);
        }
        catch (Exception exception) when (!cancellationToken.IsCancellationRequested)
        {
            LogContactFailed(logger, contactId, exception);
        }
    }

    // El id del contacto es nuestro: no dice nada de la persona, y sirve para encontrarlo en la base.
    [LoggerMessage(
        Level = LogLevel.Error,
        Message = "The WhatsApp bot could not answer the contact {ContactId}; its messages stay pending for the next round")]
    private static partial void LogContactFailed(ILogger logger, Guid contactId, Exception exception);

    [LoggerMessage(
        Level = LogLevel.Error,
        Message = "A round of the WhatsApp inbound processor failed; the pending messages are retried in the next round")]
    private static partial void LogRoundFailed(ILogger logger, Exception exception);
}
