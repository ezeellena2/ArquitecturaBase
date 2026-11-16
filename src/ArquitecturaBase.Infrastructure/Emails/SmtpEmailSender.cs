using ArquitecturaBase.Application.Interfaces.Integrations.Emails;
using ArquitecturaBase.Application.Models.Emails;
using MailKit;
using MailKit.Net.Smtp;
using MailKit.Security;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace ArquitecturaBase.Infrastructure.Emails;

/// <summary>
/// Un intento por SMTP, clasificado (N5 y N8). El plazo propio cuenta desde que empieza a conectar. Conectar y autenticarse va
/// antes del cuerpo: el apagado lo corta (<see cref="EmailSendOutcome.Stopped"/>), y lo que falla ahí se reintenta, porque no
/// salió nada. Desde el primer comando del mensaje, sólo cuenta el plazo propio: una respuesta del servidor que lo rechaza dice
/// que no lo tomó, también un 4xx después del punto final (RFC 5321): un 4xx se reintenta y un 5xx no. Un 5xx al remitente
/// (MAIL FROM) es <see cref="EmailSendOutcome.SenderRejected"/>, que la salud cuenta. Cualquier otro corte deja el envío
/// incierto, que no se repite.
/// </summary>
internal sealed class SmtpEmailSender(IOptions<SmtpOptions> options, TimeSpan deadline) : IEmailSender
{
    /// <summary>
    /// N8: el plazo de todo el envío, desde que empieza a conectar. Con lo que el worker tarda en anotarlo y completarlo
    /// (<see cref="EmailBackgroundService.SettleTimeout"/>), cabe con margen en los 25 s que el host da para apagarse
    /// (<c>HostOptions.ShutdownTimeout</c> en Program.cs, que frena los workers a la vez): un correo cuyo cuerpo empezó a salir
    /// termina antes de que el host corte. Es también el de cada operación de MailKit, que por defecto espera 2 minutos: el
    /// worker manda de a uno, y un servidor callado frenaría la cola.
    /// </summary>
    internal static readonly TimeSpan DefaultDeadline = TimeSpan.FromSeconds(12);

    [ActivatorUtilitiesConstructor]
    public SmtpEmailSender(IOptions<SmtpOptions> options) : this(options, DefaultDeadline)
    {
    }

    public async Task<EmailSendResult> SendAsync(EmailMessage message, CancellationToken cancellationToken)
    {
        var settings = options.Value;
        using var mime = MimeMessageFactory.Create(message, settings);
        using var client = new SmtpClient { Timeout = (int)deadline.TotalMilliseconds };
        // El reloj del sistema, como los plazos de los clientes HTTP: no suma timers al reloj falso de los tests.
        using var ownDeadline = new CancellationTokenSource(deadline);
        using var beforeBody = CancellationTokenSource.CreateLinkedTokenSource(ownDeadline.Token, cancellationToken);

        try
        {
            await client.ConnectAsync(settings.Host, settings.Port, settings.Security, beforeBody.Token);
            await client.AuthenticateAsync(settings.UserName, settings.Password, beforeBody.Token);
        }
        catch (Exception exception) when (cancellationToken.IsCancellationRequested && exception is not ObjectDisposedException)
        {
            // El apagado lo cortó, sea cual sea la forma en que MailKit lo dice (una cancelación o el socket cerrado): no salió nada.
            return EmailSendResult.Failed(EmailSendOutcome.Stopped);
        }
        catch (AuthenticationException)
        {
            // Un 535: la contraseña de aplicación se revocó o cambió. La salud lo marca enseguida.
            return EmailSendResult.Failed(EmailSendOutcome.AuthenticationFailed);
        }
        catch (SmtpCommandException exception) when (IsTransient(exception.StatusCode))
        {
            return EmailSendResult.Failed(EmailSendOutcome.TemporarilyRejected, (int)exception.StatusCode);
        }
        catch (Exception exception) when (exception is not ObjectDisposedException)
        {
            // La red, el TLS, el saludo del servidor o el plazo: no salió nada.
            return EmailSendResult.Failed(
                EmailSendOutcome.ConnectionFailed, exception is SmtpCommandException command ? (int)command.StatusCode : null);
        }

        // Se apagó justo al terminar de autenticarse: todavía no salió nada.
        if (cancellationToken.IsCancellationRequested) return EmailSendResult.Failed(EmailSendOutcome.Stopped);

        try
        {
            await client.SendAsync(mime, ownDeadline.Token);
        }
        catch (SmtpCommandException exception)
            when (exception.ErrorCode == SmtpErrorCode.SenderNotAccepted && !IsTransient(exception.StatusCode))
        {
            // N5: rechazó al remitente (Gmail contesta 550 5.4.5 a MAIL FROM con el cupo diario agotado): todos los correos
            // fallarían igual por ahora. No se reintenta, y la salud lo cuenta como una falla del servidor.
            return EmailSendResult.Failed(EmailSendOutcome.SenderRejected, (int)exception.StatusCode);
        }
        catch (SmtpCommandException exception)
        {
            // El servidor contestó que no lo aceptó: no salió.
            return EmailSendResult.Failed(
                IsTransient(exception.StatusCode) ? EmailSendOutcome.TemporarilyRejected : EmailSendOutcome.Rejected,
                (int)exception.StatusCode);
        }
        catch (ServiceNotAuthenticatedException)
        {
            // Un 530 antes del cuerpo: el servidor pide las credenciales otra vez. No salió nada, y se reintenta como un 535.
            return EmailSendResult.Failed(EmailSendOutcome.AuthenticationFailed);
        }
        catch (ServiceNotConnectedException)
        {
            // La conexión ya no estaba antes de mandar el mensaje: no salió nada.
            return EmailSendResult.Failed(EmailSendOutcome.ConnectionFailed);
        }
        catch (NotSupportedException)
        {
            // El destinatario pide SMTPUTF8 y el servidor no lo tiene: no se manda nada, y volver a probar daría lo mismo.
            return EmailSendResult.Failed(EmailSendOutcome.Rejected);
        }
        catch (Exception exception) when (exception is not ObjectDisposedException)
        {
            // Se cortó o venció el plazo mientras salía: pudo haber llegado.
            return EmailSendResult.Failed(EmailSendOutcome.Uncertain);
        }

        try
        {
            await client.DisconnectAsync(quit: true, ownDeadline.Token);
        }
        catch (Exception exception) when (exception is not ObjectDisposedException)
        {
            // Ya lo aceptó: un QUIT que no contesta no cambia nada.
        }

        return EmailSendResult.Sent(mime.MessageId!);
    }

    private static bool IsTransient(SmtpStatusCode statusCode) => (int)statusCode is >= 400 and < 500;
}
