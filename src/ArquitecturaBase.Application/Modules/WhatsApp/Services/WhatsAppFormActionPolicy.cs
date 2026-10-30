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
        if (contact is null || contact.UserId != recipientUserId || !await HasAddressAsync(contact.WaId, recipientUserId, cancellationToken))
            return WhatsAppFormActionErrors.RecipientUnavailable;
        if (!WhatsAppChatService.IsReplyWindowOpen(contact, timeProvider.GetUtcNow().UtcDateTime)) return WhatsAppFormActionErrors.WindowClosed;
        return null;
    }

    // The reply window is a later, passing condition: only the address decides whether the recipient can be reached.
    public async Task<bool> CanReachAsync(string? linkedWaId, Guid recipientUserId, CancellationToken cancellationToken) =>
        availability.IsEnabled && linkedWaId is not null && await HasAddressAsync(linkedWaId, recipientUserId, cancellationToken);

    private async Task<bool> HasAddressAsync(string? waId, Guid recipientUserId, CancellationToken cancellationToken)
    {
        var account = await users.FindByIdAsync(recipientUserId, cancellationToken);
        var phone = WhatsAppIds.Parse(phoneNumbers, waId);
        return account is { IsActive: true, PhoneNumberConfirmed: true } && phone.IsSuccess && account.PhoneNumber == phone.Value.Value;
    }
}
