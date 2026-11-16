using ArquitecturaBase.Application.Models.Emails;
namespace ArquitecturaBase.Application.Interfaces.Integrations.Emails;

/// <summary>Envía un email. Cambiar de proveedor es escribir otra implementación.</summary>
public interface IEmailSender
{
    /// <summary>
    /// Un intento, con el resultado clasificado (N5 y N8): los rechazos y las fallas son un resultado, no una excepción.
    /// <paramref name="cancellationToken"/> es el apagado y corta sólo antes de que salga el cuerpo
    /// (<see cref="EmailSendOutcome.Stopped"/>); desde ahí el envío termina con su propio plazo, menor que el apagado del host.
    /// </summary>
    Task<EmailSendResult> SendAsync(EmailMessage message, CancellationToken cancellationToken);
}
