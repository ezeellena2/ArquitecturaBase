using ArquitecturaBase.Domain.Authentication;

namespace ArquitecturaBase.Application.Interfaces.Persistence;

/// <summary>
/// Los enlaces se buscan por el hash del token y por la cuenta. El lock es por cuenta: lo toman la emisión (para los
/// límites y la invalidación de los anteriores) y el canje (para que un enlace sirva una sola vez).
/// </summary>
public interface ILoginLinkRepository
{
    /// <summary>
    /// Pone en fila las emisiones y los canjes de enlaces de una misma cuenta hasta que termine la unidad de trabajo.
    /// Sin esto, dos emisiones simultáneas se saltean los límites, y dos canjes simultáneos del mismo enlace entran los
    /// dos.
    /// </summary>
    Task LockAccountAsync(Guid userId, CancellationToken cancellationToken);

    /// <summary>
    /// De qué cuenta es el enlace con ese hash, o null si no hay ninguno. No deja el enlace en memoria: el canje lo
    /// vuelve a leer con <see cref="GetByTokenHashAsync"/> después de tomar el lock, así ve lo que guardó otro canje.
    /// </summary>
    Task<Guid?> FindUserIdAsync(string tokenHash, CancellationToken cancellationToken);

    /// <summary>El enlace con ese hash, sirva o no, o null si no hay ninguno.</summary>
    Task<LoginLink?> GetByTokenHashAsync(string tokenHash, CancellationToken cancellationToken);

    /// <summary>Los enlaces todavía activos de la cuenta, para invalidarlos cuando se emite uno nuevo.</summary>
    Task<IReadOnlyList<LoginLink>> ListActiveAsync(Guid userId, DateTime nowUtc, CancellationToken cancellationToken);

    /// <summary>
    /// Los enlaces sin consumir ni invalidar de la cuenta, incluso los vencidos. Se siguen en la misma unidad de
    /// trabajo para persistir su invalidación al revocar las sesiones.
    /// </summary>
    Task<IReadOnlyList<LoginLink>> ListPendingAsync(Guid userId, CancellationToken cancellationToken);

    /// <summary>
    /// Cuándo se emitió cada enlace de la cuenta desde <paramref name="sinceUtc"/>, del más viejo al más nuevo. Lo usan
    /// los límites de la sección 13 del spec del ingreso con WhatsApp.
    /// </summary>
    Task<IReadOnlyList<DateTime>> ListIssueTimesSinceAsync(Guid userId, DateTime sinceUtc, CancellationToken cancellationToken);

    void Add(LoginLink loginLink);
}
