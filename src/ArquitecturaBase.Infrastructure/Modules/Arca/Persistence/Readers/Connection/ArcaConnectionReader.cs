using ArquitecturaBase.Application.Modules.Arca.Interfaces.Persistence.Connection;
using ArquitecturaBase.Application.Modules.Arca.Models.Connection;
using ArquitecturaBase.Domain.Modules.Arca.Connection;
using ArquitecturaBase.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace ArquitecturaBase.Infrastructure.Modules.Arca.Persistence.Readers.Connection;

internal sealed class ArcaConnectionReader(ApplicationDbContext dbContext) : IArcaConnectionReader
{
    public async Task<ArcaConnectionRow?> FindAsync(CancellationToken cancellationToken)
    {
        var connection = await dbContext.Set<ArcaConnection>().AsNoTracking()
            .Select(x => new { x.Id, x.Mode, x.Environment, x.Cuit, x.Status, x.EnabledServices, x.LastTestPassed, x.LastTestedAtUtc, x.Revision })
            .SingleOrDefaultAsync(cancellationToken);
        if (connection is null) return null;

        var certificates = await dbContext.Set<ArcaCertificate>().AsNoTracking()
            .Where(x => x.ConnectionId == connection.Id)
            .OrderBy(x => x.Role)
            .Select(x => new ArcaCertificateRow(x.Id, x.Role, x.Alias, x.CertificatePem != null, x.NotBeforeUtc, x.NotAfterUtc, x.Thumbprint))
            .ToListAsync(cancellationToken);

        return new ArcaConnectionRow(connection.Id, connection.Mode, connection.Environment, connection.Cuit, connection.Status,
            connection.EnabledServices, connection.LastTestPassed, connection.LastTestedAtUtc, connection.Revision, certificates);
    }

    public Task<ArcaTicketRow?> FindTicketAsync(Guid certificateId, ArcaServiceCode service, CancellationToken cancellationToken) =>
        dbContext.Set<ArcaAccessTicket>().AsNoTracking()
            .Where(x => x.CertificateId == certificateId && x.ServiceCode == service.Value)
            .Select(x => new ArcaTicketRow(x.ProtectedCredentials, x.ExpiresAtUtc, x.BlockedUntilUtc))
            .SingleOrDefaultAsync(cancellationToken);
}
