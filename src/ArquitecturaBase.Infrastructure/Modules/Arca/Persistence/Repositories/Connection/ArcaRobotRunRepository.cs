using ArquitecturaBase.Application.Modules.Arca.Interfaces.Persistence.Connection;
using ArquitecturaBase.Domain.Modules.Arca.Connection;
using ArquitecturaBase.Infrastructure.Persistence;
using ArquitecturaBase.Infrastructure.Persistence.Extensions;
using Microsoft.EntityFrameworkCore;

namespace ArquitecturaBase.Infrastructure.Modules.Arca.Persistence.Repositories.Connection;

internal sealed class ArcaRobotRunRepository(ApplicationDbContext dbContext) : IArcaRobotRunRepository
{
    public Task<ArcaRobotRun?> GetAsync(Guid connectionId, ArcaRobotSide side, CancellationToken cancellationToken) =>
        dbContext.Set<ArcaRobotRun>().SingleOrDefaultAsync(x => x.ConnectionId == connectionId && x.Side == side, cancellationToken);

    public Task<ArcaRobotRun?> GetAsync(Guid runId, CancellationToken cancellationToken) =>
        dbContext.Set<ArcaRobotRun>().SingleOrDefaultAsync(x => x.Id == runId, cancellationToken);

    public void Add(ArcaRobotRun run) { dbContext.RequireTransaction(); dbContext.Set<ArcaRobotRun>().Add(run); }
}
