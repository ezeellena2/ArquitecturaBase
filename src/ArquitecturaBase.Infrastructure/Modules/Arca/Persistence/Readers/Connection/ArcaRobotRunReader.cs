using ArquitecturaBase.Application.Modules.Arca.Interfaces.Persistence.Connection;
using ArquitecturaBase.Application.Modules.Arca.Models.Connection;
using ArquitecturaBase.Domain.Modules.Arca.Connection;
using ArquitecturaBase.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace ArquitecturaBase.Infrastructure.Modules.Arca.Persistence.Readers.Connection;

/// <summary>Hay una sola conexión por instalación y sus corridas se borran con ella: todas las filas son de la conexión vigente.</summary>
internal sealed class ArcaRobotRunReader(ApplicationDbContext dbContext) : IArcaRobotRunReader
{
    public async Task<IReadOnlyList<ArcaRobotRunRow>> ListAsync(CancellationToken cancellationToken)
    {
        var runs = await dbContext.Set<ArcaRobotRun>().AsNoTracking().OrderBy(x => x.Side).ToListAsync(cancellationToken);
        return [.. runs.Select(run => new ArcaRobotRunRow(run.Id, run.Side, run.Status, run.CompletedSteps, run.CurrentStep, run.Outputs, run.Failure,
            run.FailedStep, run.Attempts, run.NextAttemptAtUtc, run.LeaseExpiresAtUtc, run.StartedAtUtc, run.FinishedAtUtc))];
    }

    public async Task<IReadOnlyList<Guid>> ListDuePlatformRunsAsync(DateTime nowUtc, CancellationToken cancellationToken) =>
        await dbContext.Set<ArcaRobotRun>().AsNoTracking()
            .Where(x => x.Side == ArcaRobotSide.Platform
                && ((x.Status == ArcaRobotRunStatus.Waiting && x.NextAttemptAtUtc <= nowUtc)
                    || (x.Status == ArcaRobotRunStatus.Running && x.LeaseExpiresAtUtc <= nowUtc)))
            .Select(x => x.Id)
            .ToListAsync(cancellationToken);
}
