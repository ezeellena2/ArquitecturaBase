using ArquitecturaBase.Domain.Results;

namespace ArquitecturaBase.Application.Modules.Arca.Interfaces.Services.Connection;

/// <summary>
/// Desconectar ARCA con la revisión que vio la pantalla: borra la conexión con sus certificados y deja un log con quién lo hizo
/// (H26). Va aparte del servicio de la conexión, por responsabilidad: borrar no arma la respuesta de la conexión.
/// </summary>
public interface IArcaDisconnectionService
{
    Task<Result> DisconnectAsync(long expectedRevision, CancellationToken cancellationToken);
}
