using ArquitecturaBase.Application.Interfaces.Integrations.Emails;
using ArquitecturaBase.Application.Interfaces.Services;
using ArquitecturaBase.Application.Models.Emails;
using ArquitecturaBase.Infrastructure.Delivery;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace ArquitecturaBase.Infrastructure.Emails;

/// <summary>
/// Manda los correos de la cola de a uno, un intento por reserva (N5 y N8):
/// <list type="bullet">
/// <item>lo que el servidor no tomó por algo que puede pasar (la conexión, las credenciales o un 4xx, también después del
/// cuerpo) vuelve a la cola con una espera que se duplica en cada intento, de <c>Email:RetryDelaySeconds</c> a
/// <see cref="LongestRetryWait"/>, hasta que vence; si vence sin salir, una invitación queda fallida; si no se pudo devolver,
/// la fila queda reservada y no se anota nada;</item>
/// <item>lo que el servidor rechaza para siempre (un 5xx) no se reintenta, y una invitación queda fallida;</item>
/// <item>lo que se cortó mientras salía el cuerpo pudo haber llegado: no se repite, y una invitación queda sin confirmar;</item>
/// <item>el envío no usa el token del apagado salvo antes del cuerpo: si la Api se apaga antes, el correo vuelve a la cola
/// para otra copia (<see cref="IMessageSource{T}.GiveBackCurrent"/>); desde ahí termina con el plazo propio del remitente,
/// menor que el apagado del host.</item>
/// </list>
/// Las credenciales rechazadas, el remitente rechazado y las fallas seguidas van a <see cref="EmailHealth"/>, y cada resultado a
/// <see cref="EmailMetrics"/>. Los logs llevan el id del correo y el código SMTP, nunca el destinatario ni el contenido.
/// </summary>
internal sealed partial class EmailBackgroundService(
    IMessageSource<EmailMessage> queue,
    IServiceScopeFactory scopeFactory,
    IOptions<EmailOptions> options,
    EmailHealth health,
    EmailMetrics metrics,
    ILogger<EmailBackgroundService> logger)
    : BackgroundService
{
    /// <summary>
    /// N5: la espera más larga entre dos intentos de un mismo correo. El correo de un código vence en la cola cuando se puede
    /// pedir otro (un minuto), así que la alcanza sólo una invitación, que reintenta así durante su día sin agotar los ingresos
    /// fallidos que tolera Gmail.
    /// </summary>
    public static readonly TimeSpan LongestRetryWait = TimeSpan.FromMinutes(15);

    /// <summary>
    /// H8 y N8: lo que sigue al envío (anotar el resultado y completar la fila, o devolverla a esperar) comparte un solo plazo,
    /// independiente del apagado. Con el del envío (<see cref="SmtpEmailSender.DefaultDeadline"/>), cabe con margen en los 25 s
    /// que el host da para apagarse.
    /// </summary>
    internal static readonly TimeSpan SettleTimeout = TimeSpan.FromSeconds(10);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try
        {
            await foreach (var message in queue.ReadAllAsync(stoppingToken))
            {
                if (await SendAsync(message, stoppingToken)) continue;

                // N8: la Api se apagó antes de que saliera el cuerpo: vuelve a la cola para otra copia y no se toma nada nuevo.
                queue.GiveBackCurrent();
                break;
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            // Se apagó mientras la cola esperaba o reservaba: no tenía ningún correo en la mano, y el worker termina limpio.
        }
    }

    /// <summary>Si el correo quedó resuelto (salió, vuelve a esperar, no salió o no se sabe); false si se devuelve a la cola.</summary>
    private async Task<bool> SendAsync(EmailMessage message, CancellationToken stoppingToken)
    {
        EmailSendResult result;

        try
        {
            // Reservado justo cuando la Api se apagaba: no se empieza.
            stoppingToken.ThrowIfCancellationRequested();

            await using var scope = scopeFactory.CreateAsyncScope();
            result = await scope.ServiceProvider.GetRequiredService<IEmailSender>().SendAsync(message, stoppingToken);
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            // El apagado corta sólo antes del cuerpo: no salió.
            result = EmailSendResult.Failed(EmailSendOutcome.Stopped);
        }
        catch (Exception exception)
        {
            // El remitente traduce las respuestas del servidor y las fallas de la red: esto es un bug, o un correo que no se
            // puede armar. No se reintenta y la cola sigue.
            LogUnexpectedFailure(logger, message.Id, exception);
            metrics.Record(EmailMetrics.Rejected);
            await SettleAsync(message, (service, token) => service.RecordUnsentAsync(message, token));

            return true;
        }

        switch (result.Outcome)
        {
            case EmailSendOutcome.Sent:
                health.ReportSent();
                LogSent(logger, message.Id);
                metrics.Record(EmailMetrics.Sent);
                await SettleAsync(message, (service, token) => service.RecordSentAsync(message, result.MessageId!, token));
                return true;

            case EmailSendOutcome.Stopped:
                LogGivenBack(logger, message.Id);
                metrics.Record(EmailMetrics.Returned);
                return false;

            case EmailSendOutcome.Uncertain:
                // N8: el servidor pudo haberlo recibido: no se reintenta (llegaría dos veces) ni se informa como no enviado.
                LogUncertain(logger, message.Id);
                metrics.Record(EmailMetrics.Uncertain);
                await SettleAsync(message, (service, token) => service.RecordUncertainAsync(message, token));
                return true;
        }

        if (!result.IsRetryable)
        {
            // Rechazado para siempre (un 5xx), o un resultado que esta versión no conoce: no se insiste. Un rechazo del
            // remitente (el cupo diario de Gmail) habla del servidor y no del correo: la salud lo cuenta.
            if (result.Outcome == EmailSendOutcome.SenderRejected) health.ReportFailure(result.Outcome);
            LogRejected(logger, message.Id, result.StatusCode);
            metrics.Record(EmailMetrics.Rejected);
            await SettleAsync(message, (service, token) => service.RecordUnsentAsync(message, token));
            return true;
        }

        // N5: el servidor no lo tomó por algo que puede pasar: vuelve a la cola con una espera más larga, hasta que vence.
        health.ReportFailure(result.Outcome);
        using var settle = new CancellationTokenSource(SettleTimeout);
        switch (await queue.RetryCurrentLaterAsync(TimeSpan.FromSeconds(options.Value.RetryDelaySeconds), LongestRetryWait, settle.Token))
        {
            case DeliveryRetry.Waits:
                LogRetry(logger, message.Id, result.Outcome, result.StatusCode);
                metrics.Record(EmailMetrics.Retry);
                return true;

            case DeliveryRetry.Expired:
                LogExpired(logger, message.Id, result.Outcome, result.StatusCode);
                metrics.Record(EmailMetrics.Expired);
                await RecordAsync(message, (service, token) => service.RecordUnsentAsync(message, token), settle.Token);
                return true;

            default:
                // No se pudo devolver: no venció, así que no se anota ni se cuenta como vencido. La fila queda reservada, como
                // la deja una caída, y no se vuelve a mandar; la invitación sigue pendiente hasta que vence su fila.
                LogNotReturned(logger, message.Id, result.Outcome, result.StatusCode);
                return true;
        }
    }

    /// <summary>
    /// N8: anota lo que pasó con el correo y completa su fila con un solo token acotado, independiente del apagado, así el
    /// envío más largo y todo esto caben en el apagado del host.
    /// </summary>
    private async Task SettleAsync(EmailMessage message, Func<IEmailDeliveryService, CancellationToken, Task> record)
    {
        using var settle = new CancellationTokenSource(SettleTimeout);
        await RecordAsync(message, record, settle.Token);
        await queue.CompleteCurrentAsync(settle.Token);
    }

    /// <summary>
    /// Anota en su propio scope lo que pasó con el correo; hoy sólo cambia algo para una invitación. Con el token acotado de
    /// quien llama, también mientras la Api se apaga. Si falla, se registra y la cola sigue: el correo no se vuelve a mandar, y
    /// lo único que se pierde es el estado que ve el administrador.
    /// </summary>
    private async Task RecordAsync(
        EmailMessage message, Func<IEmailDeliveryService, CancellationToken, Task> record, CancellationToken cancellationToken)
    {
        if (message.Invitation is null) return;

        try
        {
            await using var scope = scopeFactory.CreateAsyncScope();
            await record(scope.ServiceProvider.GetRequiredService<IEmailDeliveryService>(), cancellationToken);
        }
        catch (Exception exception)
        {
            LogNotRecorded(logger, message.Id, exception);
        }
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "The email {EmailId} was sent")]
    private static partial void LogSent(ILogger logger, Guid emailId);

    [LoggerMessage(Level = LogLevel.Information, Message = "The email {EmailId} was given back to the queue: the host stopped before it was sent")]
    private static partial void LogGivenBack(ILogger logger, Guid emailId);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Sending the email {EmailId} failed with {Outcome} (SMTP status {StatusCode}); it waits in the queue and is retried")]
    private static partial void LogRetry(ILogger logger, Guid emailId, EmailSendOutcome outcome, int? statusCode);

    [LoggerMessage(Level = LogLevel.Error, Message = "Sending the email {EmailId} failed with {Outcome} (SMTP status {StatusCode}) and it expired before another attempt: it was dropped")]
    private static partial void LogExpired(ILogger logger, Guid emailId, EmailSendOutcome outcome, int? statusCode);

    [LoggerMessage(Level = LogLevel.Error, Message = "Sending the email {EmailId} failed with {Outcome} (SMTP status {StatusCode}) and it could not go back to the queue: it stays claimed and is not sent again")]
    private static partial void LogNotReturned(ILogger logger, Guid emailId, EmailSendOutcome outcome, int? statusCode);

    [LoggerMessage(Level = LogLevel.Error, Message = "The mail server rejected the email {EmailId} (SMTP status {StatusCode}): it was dropped")]
    private static partial void LogRejected(ILogger logger, Guid emailId, int? statusCode);

    [LoggerMessage(Level = LogLevel.Warning, Message = "The email {EmailId} may have reached the mail server: it is not sent again")]
    private static partial void LogUncertain(ILogger logger, Guid emailId);

    [LoggerMessage(Level = LogLevel.Error, Message = "Sending the email {EmailId} failed unexpectedly: it was dropped")]
    private static partial void LogUnexpectedFailure(ILogger logger, Guid emailId, Exception exception);

    [LoggerMessage(Level = LogLevel.Warning, Message = "What happened to the email {EmailId} could not be saved; it is not sent again")]
    private static partial void LogNotRecorded(ILogger logger, Guid emailId, Exception exception);
}
