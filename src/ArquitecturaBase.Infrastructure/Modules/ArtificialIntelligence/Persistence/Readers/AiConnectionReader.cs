using ArquitecturaBase.Application.Modules.ArtificialIntelligence.Interfaces.Persistence;
using ArquitecturaBase.Application.Modules.ArtificialIntelligence.Models;
using ArquitecturaBase.Domain.Modules.ArtificialIntelligence;
using ArquitecturaBase.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
namespace ArquitecturaBase.Infrastructure.Modules.ArtificialIntelligence.Persistence.Readers;
internal sealed class AiConnectionReader(ApplicationDbContext dbContext, TimeProvider timeProvider) : IAiConnectionReader
{
    public async Task<AiConnectionSnapshot> FindAsync(CancellationToken cancellationToken) =>
        await dbContext.Set<AiConnection>().AsNoTracking().Select(x => new AiConnectionSnapshot(x.Provider, x.Model, x.ProtectedApiKey, x.Enabled, x.Revision))
            .SingleOrDefaultAsync(cancellationToken) ?? new AiConnectionSnapshot(AiProvider.OpenAI, "", null, false, 0);
    public Task<AiConnectionChangeResponse?> FindChangeAsync(Guid operatorId, Guid requestId, CancellationToken cancellationToken)
    {
        var cutoff = timeProvider.GetUtcNow().UtcDateTime.AddDays(-30);
        return dbContext.Set<AiConnectionChange>().AsNoTracking().Where(x => x.OperatorId == operatorId && x.RequestId == requestId && x.CreatedAtUtc >= cutoff)
            .Select(x => new AiConnectionChangeResponse(x.RequestId, new AiConnectionResponse(x.Provider, x.Model, x.HasCredential, x.Enabled, x.Revision)))
            .SingleOrDefaultAsync(cancellationToken);
    }
}
