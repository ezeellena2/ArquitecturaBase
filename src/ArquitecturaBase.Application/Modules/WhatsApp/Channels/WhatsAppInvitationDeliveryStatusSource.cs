using ArquitecturaBase.Application.Interfaces.Channels;
using ArquitecturaBase.Application.Models.Users;
using ArquitecturaBase.Application.Modules.WhatsApp.Interfaces.Persistence;
using ArquitecturaBase.Domain.Modules.WhatsApp;
using ArquitecturaBase.Domain.Users;

namespace ArquitecturaBase.Application.Modules.WhatsApp.Channels;

/// <summary>
/// El estado de entrega de una invitación por WhatsApp: el último que avisó Meta por el webhook del mensaje saliente
/// que tiene su id. La plantilla es de marketing y Meta limita cuántas recibe cada persona, así que puede no llegar, y
/// el admin tiene que verlo. Solo lee, sin límite.
/// </summary>
internal sealed class WhatsAppInvitationDeliveryStatusSource(IWhatsAppMessageReader messages) : IInvitationDeliveryStatusSource
{
    public UserInvitationChannel Channel => UserInvitationChannel.WhatsApp;

    /// <summary>
    /// Pendiente mientras no tenga el id de Meta (la cola todavía no la mandó) o Meta no haya avisado nada; si no, el
    /// último estado registrado del saliente.
    /// </summary>
    public async Task<InvitationDeliveryStatus> FindStatusAsync(string? providerMessageId, CancellationToken cancellationToken)
    {
        if (providerMessageId is null)
        {
            return InvitationDeliveryStatus.Pending;
        }

        return await messages.FindOutboundStatusAsync(providerMessageId, cancellationToken) switch
        {
            WhatsAppMessageStatus.Sent => InvitationDeliveryStatus.Sent,
            WhatsAppMessageStatus.Delivered => InvitationDeliveryStatus.Delivered,
            WhatsAppMessageStatus.Read => InvitationDeliveryStatus.Read,
            WhatsAppMessageStatus.Failed => InvitationDeliveryStatus.Failed,
            _ => InvitationDeliveryStatus.Pending,
        };
    }
}
