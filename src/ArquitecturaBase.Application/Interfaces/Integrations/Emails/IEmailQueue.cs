using ArquitecturaBase.Application.Models.Emails;
namespace ArquitecturaBase.Application.Interfaces.Integrations.Emails;

/// <summary>
/// Encola un email para enviarlo en segundo plano: el ingreso no espera al servidor de correo. La cola vive en memoria
/// (backend.md, "Colas en memoria"): si la app se reinicia con algo en la cola, se pierde, y un correo encolado antes de
/// un commit que falla sale igual.
/// </summary>
public interface IEmailQueue
{
    /// <summary>
    /// Nunca espera. Devuelve <c>false</c> si el correo no entró (la cola está llena): quien llama sabe así si va a salir,
    /// por ejemplo para marcar el código como enviado solo si entró, o la invitación como no enviada si no.
    /// </summary>
    bool TryEnqueue(EmailMessage message);
}
