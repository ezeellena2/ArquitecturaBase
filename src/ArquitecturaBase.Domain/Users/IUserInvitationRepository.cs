namespace ArquitecturaBase.Domain.Users;

/// <summary>
/// Las invitaciones se buscan por la cuenta (la última, para el detalle y para la espera entre una y otra) y por su id
/// (lo que manda la cola de WhatsApp). El lock es por cuenta.
/// </summary>
public interface IUserInvitationRepository
{
    /// <summary>
    /// Pone en fila, hasta que termine la unidad de trabajo, lo que se hace con las invitaciones de una cuenta. Lo toman
    /// quien invita, antes de mirar la espera y de encolar el mensaje, y la cola de WhatsApp, antes de buscar la invitación
    /// que acaba de mandar. Sin esto, dos reenvíos simultáneos se saltean la espera, y la cola puede buscar la invitación
    /// antes de que se confirme la transacción que la guarda, y no encontrarla.
    /// </summary>
    Task LockAccountAsync(Guid userId, CancellationToken cancellationToken);

    Task<UserInvitation?> GetByIdAsync(Guid invitationId, CancellationToken cancellationToken);

    /// <summary>La invitación más nueva de la cuenta, se haya podido mandar o no; null si nunca se la invitó.</summary>
    Task<UserInvitation?> GetLatestAsync(Guid userId, CancellationToken cancellationToken);

    /// <summary>La invitación más nueva de la cuenta que no falló: la que cuenta para la espera hasta la próxima.</summary>
    Task<UserInvitation?> GetLatestSentAsync(Guid userId, CancellationToken cancellationToken);

    void Add(UserInvitation invitation);
}
