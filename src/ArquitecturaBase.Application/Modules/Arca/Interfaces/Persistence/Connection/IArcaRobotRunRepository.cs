using ArquitecturaBase.Domain.Modules.Arca.Connection;

namespace ArquitecturaBase.Application.Modules.Arca.Interfaces.Persistence.Connection;

/// <summary>Los dos lados del robot de la conexión (ADR 0026). Se escriben con el lock de la conexión tomado.</summary>
public interface IArcaRobotRunRepository
{
    Task<ArcaRobotRun?> GetAsync(Guid connectionId, ArcaRobotSide side, CancellationToken cancellationToken);

    Task<ArcaRobotRun?> GetAsync(Guid runId, CancellationToken cancellationToken);

    void Add(ArcaRobotRun run);
}
