using ArquitecturaBase.Application.Models.Users;

namespace ArquitecturaBase.Application.Interfaces.Persistence;

/// <summary>
/// Lecturas de invitaciones para mostrar, sin seguimiento. Para el lock, la espera entre una y otra y lo que manda la
/// cola, <see cref="IUserInvitationRepository"/>.
/// </summary>
public interface IUserInvitationReader
{
    /// <summary>
    /// La invitación más nueva de la cuenta, se haya podido mandar o no, con el id que le dio el proveedor; null si nunca
    /// se la invitó. Es una sola consulta: el estado de entrega lo da aparte la fuente del canal
    /// (<c>IInvitationDeliveryStatusSource</c>).
    /// </summary>
    Task<UserInvitationRow?> FindLatestAsync(Guid userId, CancellationToken cancellationToken);
}
