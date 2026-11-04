using ArquitecturaBase.Application.Modules.Arca.Interfaces.Persistence.Registry;
using ArquitecturaBase.Domain.Modules.Arca.Registry;
using ArquitecturaBase.Infrastructure.Persistence;
using ArquitecturaBase.Infrastructure.Persistence.Extensions;
using Microsoft.EntityFrameworkCore;

namespace ArquitecturaBase.Infrastructure.Modules.Arca.Persistence.Repositories.Registry;

internal sealed class ArcaRegistryRepository(ApplicationDbContext dbContext) : IArcaRegistryRepository
{
    public Task<ArcaRegistryCompany?> GetCompanyAsync(Guid connectionId, CancellationToken cancellationToken) =>
        dbContext.Set<ArcaRegistryCompany>().SingleOrDefaultAsync(x => x.ConnectionId == connectionId, cancellationToken);

    public void Add(ArcaRegistryCompany company) { dbContext.RequireTransaction(); dbContext.Set<ArcaRegistryCompany>().Add(company); }
}
