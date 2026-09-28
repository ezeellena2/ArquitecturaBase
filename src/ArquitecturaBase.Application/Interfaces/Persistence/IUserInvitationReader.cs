using ArquitecturaBase.Application.Models.Users;

namespace ArquitecturaBase.Application.Interfaces.Persistence;

/// <summary>
/// Lecturas de invitaciones para mostrar, sin seguimiento. Para el lock, la espera entre una y otra y lo que manda la
/// cola, <see cref="IUserInvitationRepository"/>.
/// </summary>
public interface IUserInvitationReader
{
    /// <summary>
    /// La invitación más nueva de la cuenta, se haya podido mandar o no, con el estado de su mensaje saliente si salió
    /// por WhatsApp; null si nunca se la invitó.
    /// </summary>
    Task<UserInvitationRow?> FindLatestAsync(Guid userId, CancellationToken cancellationToken);
}
