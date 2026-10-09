using ArquitecturaBase.Application.Modules.ArtificialIntelligence.Interfaces.Persistence;
using ArquitecturaBase.Application.Modules.ArtificialIntelligence.Models;
using ArquitecturaBase.Domain.Modules.ArtificialIntelligence;
using ArquitecturaBase.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
namespace ArquitecturaBase.Infrastructure.Modules.ArtificialIntelligence.Persistence.Readers;
internal sealed class AiBusinessInformationReader(ApplicationDbContext dbContext, TimeProvider timeProvider) : IAiBusinessInformationReader
{
    public async Task<AiBusinessInformationResponse> FindAsync(CancellationToken cancellationToken) =>
        await dbContext.Set<AiBusinessInformation>().AsNoTracking().Select(x => new AiBusinessInformationResponse(x.Name, x.Description, x.Hours,
            x.Location, x.Services, x.Policies, x.Faq, x.Revision, x.Details)).SingleOrDefaultAsync(cancellationToken) ?? AiBusinessInformationResponse.Empty;
    public Task<AiBusinessInformationChangeResponse?> FindChangeAsync(Guid operatorId, Guid requestId, CancellationToken cancellationToken)
    {
        var cutoff = timeProvider.GetUtcNow().UtcDateTime.AddDays(-30);
        return dbContext.Set<AiBusinessInformationChange>().AsNoTracking().Where(x => x.OperatorId == operatorId && x.RequestId == requestId && x.CreatedAtUtc >= cutoff)
            .Select(x => new AiBusinessInformationChangeResponse(x.RequestId, x.Revision)).SingleOrDefaultAsync(cancellationToken);
    }
}
