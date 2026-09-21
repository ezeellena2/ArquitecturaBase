using ArquitecturaBase.Domain.Authentication;
using Microsoft.EntityFrameworkCore;

namespace ArquitecturaBase.Infrastructure.Persistence.Repositories;

internal sealed class LoginAuditRepository(ApplicationDbContext dbContext) : ILoginAuditRepository
{
    public void Add(LoginAudit audit) => dbContext.LoginAudits.Add(audit);

    public Task<DateTime?> GetLastSuccessAtUtcAsync(Guid userId, CancellationToken cancellationToken) =>
        dbContext.LoginAudits
            .AsNoTracking()
            .Where(audit => audit.UserId == userId && audit.Succeeded)
            .OrderByDescending(audit => audit.OccurredAtUtc)
            .Select(audit => (DateTime?)audit.OccurredAtUtc)
            .FirstOrDefaultAsync(cancellationToken);
}
