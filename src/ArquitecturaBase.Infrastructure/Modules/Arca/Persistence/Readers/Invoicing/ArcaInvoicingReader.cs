using ArquitecturaBase.Application.Modules.Arca.Interfaces.Persistence.Invoicing;
using ArquitecturaBase.Application.Modules.Arca.Models.Invoicing;
using ArquitecturaBase.Domain.Modules.Arca.Invoicing;
using ArquitecturaBase.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace ArquitecturaBase.Infrastructure.Modules.Arca.Persistence.Readers.Invoicing;

internal sealed class ArcaInvoicingReader(ApplicationDbContext dbContext) : IArcaInvoicingReader
{
    public Task<ArcaInvoicingSettingsRow?> FindSettingsAsync(Guid connectionId, CancellationToken cancellationToken) =>
        dbContext.Set<ArcaInvoicingSettings>().AsNoTracking()
            .Where(x => x.ConnectionId == connectionId)
            .Select(x => new ArcaInvoicingSettingsRow(x.PointOfSale, x.GrossIncomeNumber, x.ActivityStartOn, x.Revision))
            .SingleOrDefaultAsync(cancellationToken);
}
