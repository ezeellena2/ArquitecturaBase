using ArquitecturaBase.Domain.Authentication;

namespace ArquitecturaBase.Application.Interfaces.Persistence;

public interface ILoginAuditRepository
{
    void Add(LoginAudit audit);

    /// <summary>Cuándo ingresó bien por última vez, o null si nunca lo hizo.</summary>
    Task<DateTime?> GetLastSuccessAtUtcAsync(Guid userId, CancellationToken cancellationToken);
}
