using ArquitecturaBase.Application.Interfaces.Integrations.Phones;
using ArquitecturaBase.Application.Interfaces.Persistence;
using ArquitecturaBase.Application.Modules.WhatsApp.Interfaces.Integrations;
using ArquitecturaBase.Domain.Modules.WhatsApp;
using ArquitecturaBase.Domain.Results;
namespace ArquitecturaBase.Application.Modules.WhatsApp.Services;

internal sealed class WhatsAppFormActionPolicy(IUserReader users, IPhoneNumberParser phoneNumbers,
    IWhatsAppAvailability availability, TimeProvider timeProvider)
{
    public async Task<Error?> ValidateAsync(WhatsAppContact? contact, Guid recipientUserId, WhatsAppOutgoingMessage? accepted,
        CancellationToken cancellationToken)
    {
        if (!availability.IsEnabled) return WhatsAppFormActionErrors.ChannelUnavailable;
        if (accepted is not null && (contact is null || contact.UserId != recipientUserId || accepted.ContactId != contact.Id
            || accepted.RecipientLinkRevision != contact.LinkRevision || accepted.RecipientWaId != contact.WaId))
            return WhatsAppFormActionErrors.RecipientChanged;
        if (contact is null || contact.UserId != recipientUserId) return WhatsAppFormActionErrors.RecipientUnavailable;
        var account = await users.FindByIdAsync(recipientUserId, cancellationToken);
        var phone = WhatsAppIds.Parse(phoneNumbers, contact.WaId);
        if (account is not { IsActive: true, PhoneNumberConfirmed: true } || phone.IsFailure || account.PhoneNumber != phone.Value.Value)
            return WhatsAppFormActionErrors.RecipientUnavailable;
        if (!WhatsAppChatService.IsReplyWindowOpen(contact, timeProvider.GetUtcNow().UtcDateTime)) return WhatsAppFormActionErrors.WindowClosed;
        return null;
    }
}
