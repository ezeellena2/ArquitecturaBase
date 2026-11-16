using ArquitecturaBase.Application.Interfaces.Channels;
using ArquitecturaBase.Application.Models.Users;
using ArquitecturaBase.Domain.Delivery;
using ArquitecturaBase.Domain.Users;

namespace ArquitecturaBase.Application.Channels;

/// <summary>
/// N5: el estado de una invitación por correo, que da la cola. Con el Message-ID, el servidor de correo la aceptó (Sent). Sin
/// él, espera en la cola, que la reintenta (Pending), pero sólo mientras su fila puede vivir: un día desde que se guardó
/// (<see cref="QueuedDelivery.DefaultLifetime"/>). Después nadie sabe si salió, porque una reserva abandonada a mitad del envío
/// pudo haber llegado (Uncertain). Que no salió o que se cortó lo anota el worker en la invitación (SendFailed, SendUncertain), y
/// esas no llegan acá. Sin lecturas: el id ya viene en la fila.
/// </summary>
internal sealed class EmailInvitationDeliveryStatusSource(TimeProvider timeProvider) : IInvitationDeliveryStatusSource
{
    public UserInvitationChannel Channel => UserInvitationChannel.Email;

    public Task<InvitationDeliveryStatus> FindStatusAsync(string? providerMessageId, DateTime sentAtUtc, CancellationToken cancellationToken) =>
        Task.FromResult(providerMessageId is not null ? InvitationDeliveryStatus.Sent
            : timeProvider.GetUtcNow().UtcDateTime < sentAtUtc + QueuedDelivery.DefaultLifetime ? InvitationDeliveryStatus.Pending
            : InvitationDeliveryStatus.Uncertain);
}
