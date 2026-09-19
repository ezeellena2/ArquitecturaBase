using ArquitecturaBase.Domain.Authentication;

namespace ArquitecturaBase.Infrastructure.Persistence.Repositories;

internal sealed class LoginAuditRepository(ApplicationDbContext dbContext) : ILoginAuditRepository
{
    public void Add(LoginAudit audit) => dbContext.LoginAudits.Add(audit);
}
