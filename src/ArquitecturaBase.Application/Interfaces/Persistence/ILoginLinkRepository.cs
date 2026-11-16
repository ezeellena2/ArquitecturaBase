using ArquitecturaBase.Domain.Authentication;

namespace ArquitecturaBase.Application.Interfaces.Persistence;

/// <summary>
/// Lo que queda de los enlaces de ingreso, retirados el 2026-11-16 (B4): nada los emite ni los canjea, y la tabla queda
/// hasta que la vacíe la retención. Siguen el lock por cuenta, que pone en fila los cambios de acceso y de número de una
/// misma cuenta, y la invalidación de los pendientes cuando la cuenta suelta su número o pierde el acceso.
/// </summary>
public interface ILoginLinkRepository
{
    /// <summary>
    /// Pone en fila los cambios de acceso y de número de una misma cuenta. Exige la transacción de
    /// <see cref="IUnitOfWork.ExecuteInTransactionAsync{TResult}"/> y dura lo que ella; sin transacción lanza
    /// <see cref="InvalidOperationException"/>.
    /// </summary>
    Task LockAccountAsync(Guid userId, CancellationToken cancellationToken);

    /// <summary>Los enlaces todavía activos de la cuenta, para invalidarlos cuando suelta su número.</summary>
    Task<IReadOnlyList<LoginLink>> ListActiveAsync(Guid userId, DateTime nowUtc, CancellationToken cancellationToken);

    /// <summary>
    /// Los enlaces sin consumir ni invalidar de la cuenta, incluso los vencidos. Quedan seguidos:
    /// <c>AccountAccessRevoker</c> los invalida y los baja el guardado final del límite.
    /// </summary>
    Task<IReadOnlyList<LoginLink>> ListPendingAsync(Guid userId, CancellationToken cancellationToken);
}
