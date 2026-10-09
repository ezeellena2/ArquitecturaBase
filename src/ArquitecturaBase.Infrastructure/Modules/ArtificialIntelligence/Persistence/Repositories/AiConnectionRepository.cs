using ArquitecturaBase.Application.Modules.ArtificialIntelligence.Interfaces.Persistence;
using ArquitecturaBase.Domain.Modules.ArtificialIntelligence;
using ArquitecturaBase.Infrastructure.Persistence;
using ArquitecturaBase.Infrastructure.Persistence.Extensions;
using Microsoft.EntityFrameworkCore;
namespace ArquitecturaBase.Infrastructure.Modules.ArtificialIntelligence.Persistence.Repositories;
internal sealed class AiConnectionRepository(ApplicationDbContext dbContext, TimeProvider timeProvider) : IAiConnectionRepository
{
    public Task LockAsync(CancellationToken cancellationToken) => dbContext.AcquireAdvisoryLocksAsync([AiAdvisoryLockKeys.Connection], cancellationToken);
    public Task<AiConnection?> GetAsync(CancellationToken cancellationToken) => dbContext.Set<AiConnection>().SingleOrDefaultAsync(cancellationToken);
    public Task<AiConnectionChange?> GetChangeAsync(Guid operatorId, Guid requestId, CancellationToken cancellationToken)
    {
        var cutoff = timeProvider.GetUtcNow().UtcDateTime.AddDays(-30);
        return dbContext.Set<AiConnectionChange>().SingleOrDefaultAsync(x => x.OperatorId == operatorId && x.RequestId == requestId && x.CreatedAtUtc >= cutoff, cancellationToken);
    }
    public void Add(AiConnection connection) { dbContext.RequireTransaction(); dbContext.Set<AiConnection>().Add(connection); }
    public void AddChange(AiConnectionChange change) { dbContext.RequireTransaction(); dbContext.Set<AiConnectionChange>().Add(change); }
    public async Task DeleteExpiredChangesAsync(CancellationToken cancellationToken)
    {
        dbContext.RequireTransaction();
        var cutoff = timeProvider.GetUtcNow().UtcDateTime.AddDays(-30);
        var expired = await dbContext.Set<AiConnectionChange>().Where(x => x.CreatedAtUtc < cutoff).ToListAsync(cancellationToken);
        dbContext.Set<AiConnectionChange>().RemoveRange(expired);
    }
}
