using ArquitecturaBase.Application.Modules.Arca.Interfaces.Persistence.Connection;
using ArquitecturaBase.Domain.Modules.Arca.Connection;
using ArquitecturaBase.Infrastructure.Persistence;
using ArquitecturaBase.Infrastructure.Persistence.Extensions;
using Microsoft.EntityFrameworkCore;

namespace ArquitecturaBase.Infrastructure.Modules.Arca.Persistence.Repositories.Connection;

internal sealed class ArcaConnectionRepository(ApplicationDbContext dbContext) : IArcaConnectionRepository
{
    public Task LockConnectionAsync(CancellationToken cancellationToken) =>
        dbContext.AcquireAdvisoryLocksAsync([ArcaLockKeys.Connection], cancellationToken);

    public Task LockTicketAsync(Guid certificateId, ArcaServiceCode service, CancellationToken cancellationToken) =>
        dbContext.AcquireAdvisoryLocksAsync([ArcaLockKeys.Ticket(certificateId, service)], cancellationToken);

    public Task<ArcaConnection?> GetConnectionAsync(CancellationToken cancellationToken) =>
        dbContext.Set<ArcaConnection>().SingleOrDefaultAsync(cancellationToken);

    public Task<ArcaCertificate?> GetCertificateAsync(Guid connectionId, ArcaCertificateRole role, CancellationToken cancellationToken) =>
        dbContext.Set<ArcaCertificate>().SingleOrDefaultAsync(x => x.ConnectionId == connectionId && x.Role == role, cancellationToken);

    public Task<ArcaAccessTicket?> GetTicketAsync(Guid certificateId, ArcaServiceCode service, CancellationToken cancellationToken) =>
        dbContext.Set<ArcaAccessTicket>().SingleOrDefaultAsync(x => x.CertificateId == certificateId && x.ServiceCode == service.Value, cancellationToken);

    public void Add(ArcaConnection connection) { dbContext.RequireTransaction(); dbContext.Set<ArcaConnection>().Add(connection); }

    public void Add(ArcaCertificate certificate) { dbContext.RequireTransaction(); dbContext.Set<ArcaCertificate>().Add(certificate); }

    public void Add(ArcaAccessTicket ticket) { dbContext.RequireTransaction(); dbContext.Set<ArcaAccessTicket>().Add(ticket); }

    public void Remove(ArcaConnection connection) { dbContext.RequireTransaction(); dbContext.Set<ArcaConnection>().Remove(connection); }

    public void Remove(ArcaCertificate certificate) { dbContext.RequireTransaction(); dbContext.Set<ArcaCertificate>().Remove(certificate); }
}
