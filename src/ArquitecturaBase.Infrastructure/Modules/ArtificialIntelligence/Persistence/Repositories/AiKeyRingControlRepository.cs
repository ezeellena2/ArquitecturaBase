using ArquitecturaBase.Application.Modules.ArtificialIntelligence.Interfaces.Persistence;
using ArquitecturaBase.Domain.Modules.ArtificialIntelligence;
using ArquitecturaBase.Infrastructure.Persistence;
using ArquitecturaBase.Infrastructure.Persistence.Extensions;
using Microsoft.EntityFrameworkCore;
namespace ArquitecturaBase.Infrastructure.Modules.ArtificialIntelligence.Persistence.Repositories;
internal sealed class AiKeyRingControlRepository(ApplicationDbContext dbContext) : IAiKeyRingControlRepository
{
    public Task LockAsync(CancellationToken cancellationToken) => dbContext.AcquireAdvisoryLocksAsync([AiAdvisoryLockKeys.KeyRing], cancellationToken);
    public async Task<IReadOnlyList<AiKeyRingControl>> ListAsync(CancellationToken cancellationToken) =>
        await dbContext.Set<AiKeyRingControl>().ToListAsync(cancellationToken);
    public void Add(AiKeyRingControl control) => dbContext.Set<AiKeyRingControl>().Add(control);
}
