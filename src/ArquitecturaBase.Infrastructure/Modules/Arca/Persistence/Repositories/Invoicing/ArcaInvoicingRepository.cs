using ArquitecturaBase.Application.Modules.Arca.Interfaces.Persistence.Invoicing;
using ArquitecturaBase.Domain.Modules.Arca.Invoicing;
using ArquitecturaBase.Infrastructure.Persistence;
using ArquitecturaBase.Infrastructure.Persistence.Extensions;
using Microsoft.EntityFrameworkCore;

namespace ArquitecturaBase.Infrastructure.Modules.Arca.Persistence.Repositories.Invoicing;

internal sealed class ArcaInvoicingRepository(ApplicationDbContext dbContext) : IArcaInvoicingRepository
{
    public Task<ArcaInvoicingSettings?> GetSettingsAsync(Guid connectionId, CancellationToken cancellationToken) =>
        dbContext.Set<ArcaInvoicingSettings>().SingleOrDefaultAsync(x => x.ConnectionId == connectionId, cancellationToken);

    public void Add(ArcaInvoicingSettings settings) { dbContext.RequireTransaction(); dbContext.Set<ArcaInvoicingSettings>().Add(settings); }
}
