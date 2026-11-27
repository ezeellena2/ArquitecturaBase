using ArquitecturaBase.Application.Modules.ArtificialIntelligence.Interfaces.Persistence;
using ArquitecturaBase.Domain.Modules.ArtificialIntelligence;
using ArquitecturaBase.Infrastructure.Persistence;
using ArquitecturaBase.Infrastructure.Persistence.Extensions;
using Microsoft.EntityFrameworkCore;

namespace ArquitecturaBase.Infrastructure.Modules.ArtificialIntelligence.Persistence.Repositories;

internal sealed class AiUsageRepository(ApplicationDbContext dbContext) : IAiUsageRepository
{
    public Task LockPricesAsync(CancellationToken cancellationToken) =>
        dbContext.AcquireAdvisoryLocksAsync([AiAdvisoryLockKeys.ModelPrices], cancellationToken);

    public Task<AiModelPrice?> GetPriceAsync(AiProvider provider, string model, CancellationToken cancellationToken) =>
        dbContext.Set<AiModelPrice>().SingleOrDefaultAsync(x => x.Provider == provider && x.Model == model, cancellationToken);

    public void AddPrice(AiModelPrice price) { dbContext.RequireTransaction(); dbContext.Set<AiModelPrice>().Add(price); }
}
