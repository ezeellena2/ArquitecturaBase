using ArquitecturaBase.Domain.Authentication;

namespace ArquitecturaBase.Application.Interfaces.Persistence;

/// <summary>
/// N16: lo que borra o limpia la retención de los datos del ingreso, de a lotes. Los <c>Remove…</c> cargan hasta
/// <c>limit</c> filas, de la más vieja a la más nueva, las marcan para borrar y devuelven cuántas: las borra el guardado
/// del límite. Todo se llama dentro del límite del lote, después de <see cref="ExistsOtherRunAsync"/>.
/// </summary>
public interface IIdentityRetentionRepository
{
    /// <summary>
    /// Si otra copia de la Api está corriendo un lote de la retención. Para saberlo intenta tomar el lock de la retención
    /// (<c>identity-retention:run</c>) sin esperar: si devuelve false, el lock quedó tomado por esta transacción hasta que
    /// termine, y dos copias nunca corren un lote a la vez. Se llama primero, y nadie más pide ese lock.
    /// </summary>
    /// <remarks>
    /// Es un <c>Exists</c> y no un <c>Lock</c> porque responde algo (ADR 0008: un <c>Lock…</c> devuelve <c>Task</c> y
    /// espera): una copia que encuentra el lock tomado termina su corrida en vez de esperar.
    /// </remarks>
    Task<bool> ExistsOtherRunAsync(CancellationToken cancellationToken);

    /// <summary>Los códigos creados antes de <paramref name="createdBeforeUtc"/>, con su destino y su dirección.</summary>
    Task<int> RemoveLoginCodesAsync(DateTime createdBeforeUtc, int limit, CancellationToken cancellationToken);

    /// <summary>
    /// Los enlaces creados antes de <paramref name="createdBeforeUtc"/>. Toma antes el lock de enlaces de cada cuenta del
    /// lote, el mismo que la revocación de su acceso, que invalida también los vencidos: así nunca borra uno que esa
    /// revocación ya cargó para invalidar.
    /// </summary>
    Task<int> RemoveLoginLinksAsync(DateTime createdBeforeUtc, int limit, CancellationToken cancellationToken);

    /// <summary>
    /// Las invitaciones mandadas antes de <paramref name="sentBeforeUtc"/> que no son la última de su cuenta, con el mismo
    /// orden que la que muestra el detalle (la fecha y, en el mismo instante, el Id).
    /// </summary>
    Task<int> RemoveUserInvitationsAsync(DateTime sentBeforeUtc, int limit, CancellationToken cancellationToken);

    /// <summary>
    /// Los ingresos anteriores a <paramref name="occurredBeforeUtc"/> que todavía guardan la IP, el navegador o, si
    /// fallaron, lo que la persona escribió (<see cref="LoginAudit.ForgetRequestInfo"/>).
    /// </summary>
    Task<IReadOnlyList<LoginAudit>> ListLoginAuditsWithRequestInfoAsync(
        DateTime occurredBeforeUtc, int limit, CancellationToken cancellationToken);
}
