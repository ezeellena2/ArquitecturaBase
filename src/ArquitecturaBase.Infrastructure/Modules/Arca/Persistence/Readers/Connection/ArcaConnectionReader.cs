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
            .Select(x => new { x.Id, x.Mode, x.Environment, x.Cuit, x.PlatformCuit, x.Status, x.EnabledServices, x.LastTestPassed, x.LastTestedAtUtc, x.Revision })
            .SingleOrDefaultAsync(cancellationToken);
        if (connection is null) return null;

        var certificates = await dbContext.Set<ArcaCertificate>().AsNoTracking()
            .Where(x => x.ConnectionId == connection.Id)
            .OrderBy(x => x.Role)
            .Select(x => new ArcaCertificateRow(x.Id, x.Role, x.Alias, x.CertificatePem != null, x.NotBeforeUtc, x.NotAfterUtc, x.Thumbprint))
            .ToListAsync(cancellationToken);

        return new ArcaConnectionRow(connection.Id, connection.Mode, connection.Environment, connection.Cuit, connection.PlatformCuit, connection.Status,
            connection.EnabledServices, connection.LastTestPassed, connection.LastTestedAtUtc, connection.Revision, certificates);
    }

    public Task<ArcaPendingRequestRow?> FindPendingRequestAsync(CancellationToken cancellationToken) =>
        dbContext.Set<ArcaCertificate>().AsNoTracking()
            .Where(x => x.Role == ArcaCertificateRole.Pending)
            .Select(x => new ArcaPendingRequestRow(x.Alias, x.RequestPem))
            .SingleOrDefaultAsync(cancellationToken);

    public Task<ArcaSigningCertificateRow?> FindSigningCertificateAsync(ArcaCertificateRole role, CancellationToken cancellationToken) =>
        (from certificate in dbContext.Set<ArcaCertificate>().AsNoTracking()
         join connection in dbContext.Set<ArcaConnection>().AsNoTracking() on certificate.ConnectionId equals connection.Id
         where certificate.Role == role && certificate.CertificatePem != null
         select new ArcaSigningCertificateRow(connection.Id, certificate.Id, connection.Cuit, connection.Environment,
             certificate.ProtectedPrivateKey, certificate.CertificatePem!, connection.PlatformCuit))
            .SingleOrDefaultAsync(cancellationToken);

    public Task<ArcaTicketRow?> FindTicketAsync(Guid certificateId, ArcaServiceCode service, CancellationToken cancellationToken) =>
        dbContext.Set<ArcaAccessTicket>().AsNoTracking()
            .Where(x => x.CertificateId == certificateId && x.ServiceCode == service.Value)
            .Select(x => new ArcaTicketRow(x.ProtectedCredentials, x.ExpiresAtUtc, x.BlockedUntilUtc))
            .SingleOrDefaultAsync(cancellationToken);
}
