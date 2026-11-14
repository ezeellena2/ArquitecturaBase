using ArquitecturaBase.Application.Interfaces.Integrations.Phones;
using ArquitecturaBase.Application.Modules.WhatsApp.Interfaces.Services;
using ArquitecturaBase.Application.Modules.WhatsApp.Models;
using ArquitecturaBase.Infrastructure.Delivery;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace ArquitecturaBase.Infrastructure.Modules.WhatsApp;

/// <summary>
/// Manda los mensajes de la cola de a uno (sección 9 del spec). Lo que no salió y vale reintentar (H7: la red no llegó a
/// Meta, el circuito o el límite cortaron antes, o Meta lo rechazó por un rato: el límite por persona, el de la cuenta,
/// un 5xx con 131000, 131016, 2 o 133004) se reintenta con espera, hasta 3 intentos en total; lo que fallaría igual (la ventana de
/// 24 horas, un número sin WhatsApp, el token o sus permisos) no se insiste, y lo que pudo haber llegado (un timeout,
/// una respuesta cortada, un 5xx sin código) no se repite. Un problema del token va además a <see cref="WhatsAppHealth"/>.
/// Cada mensaje que sale se guarda en el historial con su resumen seguro, y uno que no sale, o no se sabe, se informa
/// igual mediante <see cref="IWhatsAppDeliveryService"/>: una invitación queda fallida o sin confirmar para que el admin
/// la vea. Los logs llevan el número enmascarado, el motivo y el código de Meta: nunca el cuerpo, el código de
/// ingreso, la URL ni el token.
/// </summary>
internal sealed partial class WhatsAppSenderBackgroundService(
    IMessageSource<WhatsAppOutboundMessage> sendQueue,
    IServiceScopeFactory scopeFactory,
    IOptions<WhatsAppOptions> options,
    IPhoneNumberParser phoneNumberParser,
    WhatsAppHealth health,
    TimeProvider timeProvider,
    IWhatsAppDispatchCoordinator coordinator,
    ILogger<WhatsAppSenderBackgroundService> logger)
    : BackgroundService
{
    public const int MaxAttempts = 3;

    /// <summary>
    /// H8: lo que registra el resultado de un envío tiene su propio plazo, independiente del apagado. Va con el reloj del sistema:
    /// así no suma timers al reloj falso con el que los tests miden las esperas de los reintentos.
    /// </summary>
    private static readonly TimeSpan RecordTimeout = TimeSpan.FromSeconds(10);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await foreach (var message in sendQueue.ReadAllAsync(stoppingToken))
        {
            if (await SendWithRetriesAsync(message, stoppingToken)) continue;

            // H8: la Api se apagó mientras esperaba su turno o su reintento: vuelve a la cola para otra copia y no se toma nada nuevo.
            sendQueue.GiveBackCurrent();
            break;
        }
    }

    /// <summary>Si el mensaje terminó (salió o se informó que no); false si la Api se apagó antes de que saliera.</summary>
    private async Task<bool> SendWithRetriesAsync(WhatsAppOutboundMessage message, CancellationToken cancellationToken)
    {
        var messageType = message.GetType().Name;
        var recipient = phoneNumberParser.Mask(message.To);

        for (var attempt = 1; ; attempt++)
        {
            WhatsAppSendResult result;
            IWhatsAppDispatchLease lease;

            try
            {
                lease = await coordinator.AcquireAsync(message.To, cancellationToken);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                LogGivenBack(logger, messageType, recipient);

                return false;
            }
            catch (Exception exception)
            {
                LogUnexpectedFailure(logger, messageType, recipient, exception);
                await RecordUnsentAsync(message, messageType, recipient);

                return true;
            }

            try
            {
                // H8: con su turno tomado, la llamada termina con su plazo (los del cliente o la lease perdida), sin que la
                // corte el apagado. Un scope por intento: el HttpClient tipado es transient y el factory rota sus handlers.
                await using var scope = scopeFactory.CreateAsyncScope();
                result = await scope.ServiceProvider.GetRequiredService<IWhatsAppCloudClient>().SendAsync(message, lease.LostOwnership);
            }
            catch (OperationCanceledException)
            {
                result = WhatsAppSendResult.Uncertain();
            }
            catch (Exception exception)
            {
                // El cliente traduce las fallas de Meta y de la red: esto es un bug. Se descarta y la cola sigue.
                LogUnexpectedFailure(logger, messageType, recipient, exception);
                await ReleaseTurnAsync(lease, messageType, recipient);
                await RecordUnsentAsync(message, messageType, recipient);

                return true;
            }

            // Liberar el turno no pisa lo que pasó con el envío: si Redis falla, la lease vence sola.
            await ReleaseTurnAsync(lease, messageType, recipient);

            if (result.IsSent)
            {
                health.ReportSent();
                LogSent(logger, messageType, recipient);

                await RecordAsync(message, result.WaMessageId!, messageType, recipient);

                return true;
            }

            if (result.OutcomeUncertain)
            {
                // H7: Meta pudo haberlo recibido: no se reintenta (se mandaría dos veces) ni se informa como no enviado.
                LogUncertain(logger, messageType, recipient);
                await RecordUncertainAsync(message, messageType, recipient);

                return true;
            }

            var failure = result.Failure!.Value;

            if (failure.IsTokenProblem())
            {
                // No es este mensaje: todos van a fallar igual hasta que alguien arregle el token y reinicie la Api.
                health.ReportTokenProblem(failure);

                if (failure == WhatsAppSendFailure.InvalidToken)
                {
                    LogInvalidToken(logger, messageType, recipient, result.MetaErrorCode);
                }
                else
                {
                    LogMissingPermission(logger, messageType, recipient, result.MetaErrorCode);
                }

                await RecordUnsentAsync(message, messageType, recipient);

                return true;
            }

            if (!failure.IsRetryable())
            {
                LogNotSent(logger, messageType, recipient, failure, result.MetaErrorCode);
                await RecordUnsentAsync(message, messageType, recipient);

                return true;
            }

            if (attempt == MaxAttempts)
            {
                LogDropped(logger, messageType, recipient, MaxAttempts, failure, result.MetaErrorCode);
                await RecordUnsentAsync(message, messageType, recipient);

                return true;
            }

            // Nunca menos de 6 segundos (lo controla WhatsAppOptions al arrancar): antes, Meta rechaza otra vez un
            // mensaje a la misma persona (131056) y el intento se pierde.
            var delay = TimeSpan.FromSeconds(options.Value.RetryDelaySeconds);
            LogRetry(logger, messageType, recipient, failure, result.MetaErrorCode, attempt, MaxAttempts, delay.TotalSeconds);

            try
            {
                await Task.Delay(delay, timeProvider, cancellationToken);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                // H8: no salió en este intento, así que se devuelve en vez de quedar tomado por la copia que se apaga.
                LogGivenBack(logger, messageType, recipient);

                return false;
            }
        }
    }

    /// <summary>
    /// Guarda el mensaje recién mandado en el historial, con el id que devolvió Meta, para que los estados del webhook lo
    /// encuentren (sección 9 del spec). En su propio scope, donde IWhatsAppDeliveryService abre su límite
    /// (ExecuteInTransactionAsync); el HTTP a Meta ya salió, fuera de toda transacción. Si lanza, el mensaje ya salió:
    /// se registra y no se vuelve a mandar, porque la persona lo recibiría dos veces. Lo único que se pierde es su
    /// historial y sus estados. H8: con un token propio y acotado, también mientras la Api se apaga.
    /// </summary>
    private async Task RecordAsync(
        WhatsAppOutboundMessage message,
        string waMessageId,
        string messageType,
        string recipient)
    {
        try
        {
            using var bounded = new CancellationTokenSource(RecordTimeout);
            await using var scope = scopeFactory.CreateAsyncScope();
            var delivery = scope.ServiceProvider.GetRequiredService<IWhatsAppDeliveryService>();

            await delivery.RecordSentAsync(message, waMessageId, bounded.Token);
        }
        catch (Exception exception)
        {
            LogNotRecorded(logger, messageType, recipient, exception);
        }
    }

    private async Task RecordUncertainAsync(WhatsAppOutboundMessage message, string messageType, string recipient)
    {
        try
        {
            using var bounded = new CancellationTokenSource(RecordTimeout);
            await using var scope = scopeFactory.CreateAsyncScope();
            await scope.ServiceProvider.GetRequiredService<IWhatsAppDeliveryService>().RecordUncertainAsync(message, bounded.Token);
        }
        catch (Exception exception)
        {
            LogUncertainNotRecorded(logger, messageType, recipient, exception);
        }
    }

    private async Task ReleaseTurnAsync(IWhatsAppDispatchLease lease, string messageType, string recipient)
    {
        try
        {
            await lease.DisposeAsync();
        }
        catch (Exception exception)
        {
            LogTurnNotReleased(logger, messageType, recipient, exception);
        }
    }

    /// <summary>
    /// Informa que el mensaje no salió, en su propio scope y su propio límite, como <see cref="RecordAsync"/>, y con su
    /// mismo token acotado. Hoy solo cambia algo para una invitación, que queda como fallida. Si lanza, se registra: lo único
    /// que se pierde es que el admin vea la invitación como pendiente en lugar de fallida.
    /// </summary>
    private async Task RecordUnsentAsync(
        WhatsAppOutboundMessage message,
        string messageType,
        string recipient)
    {
        try
        {
            using var bounded = new CancellationTokenSource(RecordTimeout);
            await using var scope = scopeFactory.CreateAsyncScope();
            var delivery = scope.ServiceProvider.GetRequiredService<IWhatsAppDeliveryService>();

            await delivery.RecordUnsentAsync(message, bounded.Token);
        }
        catch (Exception exception)
        {
            LogUnsentNotRecorded(logger, messageType, recipient, exception);
        }
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "The {MessageType} to {Recipient} was given back to the queue: the host stopped before it was sent")]
    private static partial void LogGivenBack(ILogger logger, string messageType, string recipient);

    [LoggerMessage(Level = LogLevel.Warning, Message = "The turn to write to {Recipient} for the {MessageType} could not be released; it expires on its own")]
    private static partial void LogTurnNotReleased(ILogger logger, string messageType, string recipient, Exception exception);

    [LoggerMessage(Level = LogLevel.Warning, Message = "The {MessageType} to {Recipient} may have reached Meta: it is not sent again")]
    private static partial void LogUncertain(ILogger logger, string messageType, string recipient);

    [LoggerMessage(Level = LogLevel.Information, Message = "The {MessageType} to {Recipient} was sent")]
    private static partial void LogSent(ILogger logger, string messageType, string recipient);

    [LoggerMessage(
        Level = LogLevel.Warning,
        Message = "The {MessageType} to {Recipient} was sent but could not be saved in the history; it is not sent again")]
    private static partial void LogNotRecorded(ILogger logger, string messageType, string recipient, Exception exception);

    [LoggerMessage(
        Level = LogLevel.Warning,
        Message = "The {MessageType} to {Recipient} may have reached Meta and that could not be saved")]
    private static partial void LogUncertainNotRecorded(ILogger logger, string messageType, string recipient, Exception exception);

    [LoggerMessage(
        Level = LogLevel.Warning,
        Message = "The {MessageType} to {Recipient} was not sent and that could not be saved")]
    private static partial void LogUnsentNotRecorded(ILogger logger, string messageType, string recipient, Exception exception);

    [LoggerMessage(
        Level = LogLevel.Warning,
        Message = "Sending the {MessageType} to {Recipient} failed with {Failure} (Meta code {MetaErrorCode}, attempt {Attempt} of {MaxAttempts}); retrying in {DelaySeconds} s")]
    private static partial void LogRetry(
        ILogger logger,
        string messageType,
        string recipient,
        WhatsAppSendFailure failure,
        int? metaErrorCode,
        int attempt,
        int maxAttempts,
        double delaySeconds);

    [LoggerMessage(
        Level = LogLevel.Warning,
        Message = "The {MessageType} to {Recipient} was not sent: {Failure} (Meta code {MetaErrorCode})")]
    private static partial void LogNotSent(
        ILogger logger,
        string messageType,
        string recipient,
        WhatsAppSendFailure failure,
        int? metaErrorCode);

    [LoggerMessage(
        Level = LogLevel.Error,
        Message = "The {MessageType} to {Recipient} was dropped after {MaxAttempts} attempts: {Failure} (Meta code {MetaErrorCode})")]
    private static partial void LogDropped(
        ILogger logger,
        string messageType,
        string recipient,
        int maxAttempts,
        WhatsAppSendFailure failure,
        int? metaErrorCode);

    // Los dos de configuración dicen que hay que reiniciar: las opciones se leen al arrancar (IOptions), así que un
    // token nuevo o con más permisos no se usa hasta entonces.
    [LoggerMessage(
        Level = LogLevel.Error,
        Message = "WhatsApp rejected the access token (Meta code {MetaErrorCode}); the {MessageType} to {Recipient} was not sent. Load a new token in WhatsApp:AccessToken and restart the Api, which reads it at startup")]
    private static partial void LogInvalidToken(ILogger logger, string messageType, string recipient, int? metaErrorCode);

    [LoggerMessage(
        Level = LogLevel.Error,
        Message = "The WhatsApp access token lacks a permission (Meta code {MetaErrorCode}); the {MessageType} to {Recipient} was not sent. Give the system user the WhatsApp account and a token with whatsapp_business_messaging in WhatsApp:AccessToken, then restart the Api, which reads it at startup")]
    private static partial void LogMissingPermission(ILogger logger, string messageType, string recipient, int? metaErrorCode);

    [LoggerMessage(Level = LogLevel.Error, Message = "Sending the {MessageType} to {Recipient} failed unexpectedly; the message was dropped")]
    private static partial void LogUnexpectedFailure(ILogger logger, string messageType, string recipient, Exception exception);
}
