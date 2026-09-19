namespace ArquitecturaBase.Domain.Authentication;

public interface ILoginAuditRepository
{
    void Add(LoginAudit audit);
}
