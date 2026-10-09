using ArquitecturaBase.Application.Models.Emails;
namespace ArquitecturaBase.Application.Interfaces.Integrations.Emails;

/// <summary>
/// Encola contenido cifrado en la transacción del llamador, sin guardar ni llamar al servidor de correo.
/// Los pendientes confirmados sobreviven al reinicio; un rollback también revierte el envío.
/// </summary>
public interface IEmailQueue
{
    /// <summary>
    /// Devuelve <c>false</c> con capacidad agotada o contenido vencido. Puede esperar a PostgreSQL, nunca a SMTP.
    /// Aceptar el mensaje no confirma su entrega: el caso de uso conserva su política de no enviado.
    /// </summary>
    bool TryEnqueue(EmailMessage message);
}
