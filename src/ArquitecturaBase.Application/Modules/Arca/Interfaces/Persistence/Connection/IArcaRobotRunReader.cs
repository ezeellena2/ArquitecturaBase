using ArquitecturaBase.Application.Modules.Arca.Models.Connection;

namespace ArquitecturaBase.Application.Modules.Arca.Interfaces.Persistence.Connection;

public interface IArcaRobotRunReader
{
    /// <summary>Los lados del robot de la conexión de la instalación; vacío sin conexión o sin corrida.</summary>
    Task<IReadOnlyList<ArcaRobotRunRow>> ListAsync(CancellationToken cancellationToken);

    /// <summary>Los lados de la plataforma que le toca tomar a un host: en espera y vencidos, o en curso con la reserva vencida.</summary>
    Task<IReadOnlyList<Guid>> ListDuePlatformRunsAsync(DateTime nowUtc, CancellationToken cancellationToken);
}
