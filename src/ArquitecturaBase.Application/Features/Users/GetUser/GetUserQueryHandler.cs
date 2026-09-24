using ArquitecturaBase.Application.Abstractions.Identity;
using ArquitecturaBase.Application.Abstractions.Messaging;
using ArquitecturaBase.Application.Interfaces.Integrations;
using ArquitecturaBase.Application.Interfaces.Persistence;
using ArquitecturaBase.Domain.Results;
using ArquitecturaBase.Domain.Users;
using ArquitecturaBase.Domain.ValueObjects;
using ArquitecturaBase.Domain.WhatsApp;

namespace ArquitecturaBase.Application.Features.Users.GetUser;

/// <summary>
/// El detalle de un usuario, con el número formateado y su última invitación. El estado de una invitación por WhatsApp
/// sale del mensaje que guardó la cola con el id de Meta, que es el que actualiza el webhook con cada aviso.
/// </summary>
internal sealed class GetUserQueryHandler(
    IIdentityService identityService,
    IUserInvitationRepository invitations,
    IWhatsAppMessageRepository messages,
    IPhoneNumberParser phoneNumbers)
    : IQueryHandler<GetUserQuery, UserDetail>
{
    public async Task<Result<UserDetail>> Handle(GetUserQuery query, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);

        if (await identityService.FindDetailAsync(query.UserId, cancellationToken) is not { } detail)
        {
            return UserErrors.NotFound;
        }

        // Sin número, Create falla y queda en null, igual que el número.
        var phone = PhoneNumber.Create(detail.PhoneNumber);

        return detail with
        {
            FormattedPhoneNumber = phone.IsSuccess ? phoneNumbers.FormatInternational(phone.Value) : null,
            LastInvitation = await LastInvitationAsync(detail.Id, cancellationToken),
        };
    }

    private async Task<LastInvitation?> LastInvitationAsync(Guid userId, CancellationToken cancellationToken) =>
        await invitations.GetLatestAsync(userId, cancellationToken) is { } invitation
            ? new LastInvitation(invitation.Channel, invitation.SentAtUtc, await DeliveryStatusAsync(invitation, cancellationToken))
            : null;

    /// <summary>
    /// Por correo no hay estado. Por WhatsApp: fallida si no salió; pendiente mientras la cola no la mandó o Meta todavía
    /// no avisó nada; y si avisó, el último estado que avisó.
    /// </summary>
    private async Task<InvitationDeliveryStatus?> DeliveryStatusAsync(UserInvitation invitation, CancellationToken cancellationToken)
    {
        if (invitation.Channel is not UserInvitationChannel.WhatsApp)
        {
            return null;
        }

        if (invitation.SendFailed)
        {
            return InvitationDeliveryStatus.Failed;
        }

        if (invitation.WaMessageId is not { } waMessageId)
        {
            return InvitationDeliveryStatus.Pending;
        }

        var sent = (await messages.ListOutboundAsync([waMessageId], cancellationToken)).SingleOrDefault();

        return sent?.Status switch
        {
            WhatsAppMessageStatus.Sent => InvitationDeliveryStatus.Sent,
            WhatsAppMessageStatus.Delivered => InvitationDeliveryStatus.Delivered,
            WhatsAppMessageStatus.Read => InvitationDeliveryStatus.Read,
            WhatsAppMessageStatus.Failed => InvitationDeliveryStatus.Failed,
            _ => InvitationDeliveryStatus.Pending,
        };
    }
}
