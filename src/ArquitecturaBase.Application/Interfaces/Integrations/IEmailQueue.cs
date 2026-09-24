using ArquitecturaBase.Application.Models.Emails;
namespace ArquitecturaBase.Application.Interfaces.Integrations;

/// <summary>Encola un email para enviarlo en segundo plano: el ingreso no espera al servidor de correo.</summary>
public interface IEmailQueue
{
    ValueTask EnqueueAsync(EmailMessage message, CancellationToken cancellationToken);
}
