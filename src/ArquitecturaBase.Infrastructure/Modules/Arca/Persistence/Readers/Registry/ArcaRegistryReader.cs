using ArquitecturaBase.Application.Modules.Arca.Interfaces.Persistence.Registry;
using ArquitecturaBase.Application.Modules.Arca.Models.Registry;
using ArquitecturaBase.Domain.Modules.Arca.Registry;
using ArquitecturaBase.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace ArquitecturaBase.Infrastructure.Modules.Arca.Persistence.Readers.Registry;

internal sealed class ArcaRegistryReader(ApplicationDbContext dbContext) : IArcaRegistryReader
{
    public Task<ArcaRegistryCompanyRow?> FindCompanyAsync(Guid connectionId, CancellationToken cancellationToken) =>
        dbContext.Set<ArcaRegistryCompany>().AsNoTracking()
            .Where(x => x.ConnectionId == connectionId)
            .Select(x => new ArcaRegistryCompanyRow(x.LegalName, x.VatCondition, x.FiscalAddress))
            .SingleOrDefaultAsync(cancellationToken);
}
