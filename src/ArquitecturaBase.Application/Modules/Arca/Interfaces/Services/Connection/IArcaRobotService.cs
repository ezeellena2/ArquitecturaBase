using ArquitecturaBase.Application.Modules.Arca.Models.Connection;
using ArquitecturaBase.Domain.Results;

namespace ArquitecturaBase.Application.Modules.Arca.Interfaces.Services.Connection;

/// <summary>
/// El robot de delegación desde la pantalla (ADR 0026): arrancar el lado del cliente con su consentimiento y su clave,
/// seguir a mano si se frenó y leer el avance de los dos lados.
/// </summary>
public interface IArcaRobotService
{
    Task<Result<ArcaRobotRunResponse>> GetAsync(CancellationToken cancellationToken);

    /// <summary>
    /// Con el robot prendido, una conexión en modo 3 y la revisión que vio la pantalla, reserva el lado del cliente en este
    /// host, pone en espera el de la plataforma y pasa la clave al worker en memoria, después del commit.
    /// </summary>
    Task<Result<ArcaRobotRunResponse>> StartAsync(ArcaRobotStartRequest request, CancellationToken cancellationToken);

    /// <summary>
    /// El cliente frenado hizo a mano los pasos de la guía (RB3-A): su lado queda terminado y la plataforma sigue con
    /// aceptar, asignar y confirmar.
    /// </summary>
    Task<Result<ArcaRobotRunResponse>> CompleteByHandAsync(CancellationToken cancellationToken);
}
