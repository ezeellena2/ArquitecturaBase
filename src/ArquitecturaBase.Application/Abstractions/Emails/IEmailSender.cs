namespace ArquitecturaBase.Application.Abstractions.Emails;

/// <summary>Envía un email. Cambiar de proveedor es escribir otra implementación.</summary>
public interface IEmailSender
{
    Task SendAsync(EmailMessage message, CancellationToken cancellationToken);
}
