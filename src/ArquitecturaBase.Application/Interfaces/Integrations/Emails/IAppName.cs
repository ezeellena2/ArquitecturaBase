namespace ArquitecturaBase.Application.Interfaces.Integrations.Emails;

/// <summary>
/// El nombre del sistema que ven las personas: el de Configuración › Apariencia (RC6-B), el mismo de los correos. Lo usan los
/// mensajes y documentos que arma Application, como la invitación por WhatsApp y los PDF de las respuestas, y se lee en cada
/// uso: un cambio vale para lo próximo que salga, sin reiniciar nada.
/// </summary>
public interface IAppName
{
    Task<string> GetAsync(CancellationToken cancellationToken);
}
