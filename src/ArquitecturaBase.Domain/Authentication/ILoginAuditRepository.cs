namespace ArquitecturaBase.Domain.Authentication;

public interface ILoginAuditRepository
{
    void Add(LoginAudit audit);

    /// <summary>Cuándo ingresó bien por última vez, o null si nunca lo hizo.</summary>
    Task<DateTime?> GetLastSuccessAtUtcAsync(Guid userId, CancellationToken cancellationToken);
}
