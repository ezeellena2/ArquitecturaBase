using ArquitecturaBase.Application.Modules.ArtificialIntelligence.Interfaces.Persistence;
using ArquitecturaBase.Domain.Modules.ArtificialIntelligence;
using ArquitecturaBase.Infrastructure.Persistence;
using ArquitecturaBase.Infrastructure.Persistence.Extensions;
using Microsoft.EntityFrameworkCore;
namespace ArquitecturaBase.Infrastructure.Modules.ArtificialIntelligence.Persistence.Repositories;
internal sealed class AiBusinessInformationRepository(ApplicationDbContext dbContext, TimeProvider timeProvider) : IAiBusinessInformationRepository
{
    public Task LockAsync(CancellationToken cancellationToken) => dbContext.AcquireAdvisoryLocksAsync([AiAdvisoryLockKeys.BusinessInformation], cancellationToken);
    public Task<AiBusinessInformation?> GetAsync(CancellationToken cancellationToken) => dbContext.Set<AiBusinessInformation>().SingleOrDefaultAsync(cancellationToken);
    public Task<AiBusinessInformationChange?> GetChangeAsync(Guid operatorId, Guid requestId, CancellationToken cancellationToken)
    {
        var cutoff = timeProvider.GetUtcNow().UtcDateTime.AddDays(-30);
        return dbContext.Set<AiBusinessInformationChange>().SingleOrDefaultAsync(x => x.OperatorId == operatorId && x.RequestId == requestId && x.CreatedAtUtc >= cutoff, cancellationToken);
    }
    public void Add(AiBusinessInformation information) { dbContext.RequireTransaction(); dbContext.Set<AiBusinessInformation>().Add(information); }
    public void AddChange(AiBusinessInformationChange change) { dbContext.RequireTransaction(); dbContext.Set<AiBusinessInformationChange>().Add(change); }
    public async Task DeleteExpiredChangesAsync(CancellationToken cancellationToken)
    {
        dbContext.RequireTransaction();
        var cutoff = timeProvider.GetUtcNow().UtcDateTime.AddDays(-30);
        var expired = await dbContext.Set<AiBusinessInformationChange>().Where(x => x.CreatedAtUtc < cutoff).ToListAsync(cancellationToken);
        dbContext.Set<AiBusinessInformationChange>().RemoveRange(expired);
    }
}
