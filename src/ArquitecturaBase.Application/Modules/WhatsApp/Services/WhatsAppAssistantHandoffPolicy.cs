using ArquitecturaBase.Application.Modules.WhatsApp.Interfaces.Persistence;
using ArquitecturaBase.Domain.Modules.WhatsApp;

namespace ArquitecturaBase.Application.Modules.WhatsApp.Services;

/// <summary>
/// Derivar (AS4, also for the documents of the assistant): the conversation goes to Atención manual, it is marked «Necesita una
/// persona» with its reason, and the person gets a fixed message: the profile's or the caller's own text, already in the
/// person's language. It runs under the caller's lock of the contact and inside its unit of work; it never saves.
/// </summary>
internal sealed class WhatsAppAssistantHandoffPolicy(IWhatsAppConversationRepository conversations, WhatsAppFormActionPolicy recipients, TimeProvider clock)
{
    /// <summary>
    /// The mark always stays. The message goes out only when there is one (an agent without «Mensaje al derivar» only marks) and
    /// while the person can be reached (account, confirmed number and the 24-hour window); then the outgoing is returned, null
    /// otherwise.
    /// </summary>
    public async Task<WhatsAppOutgoingMessage?> HandOffAsync(WhatsAppContact contact, Guid recipientUserId, Guid requestId, string reasonCode,
        string? reason, string? message, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(contact);
        var nowUtc = clock.GetUtcNow().UtcDateTime;
        contact.HandOff(reasonCode, reason, nowUtc);
        if (string.IsNullOrWhiteSpace(message) || contact.WaId is not { } waId
            || await recipients.ValidateAsync(contact, recipientUserId, null, cancellationToken) is not null) return null;
        var outgoing = WhatsAppOutgoingMessage.CreateAssistantHandoff(recipientUserId, contact.Id, requestId, message.Trim(), nowUtc,
            contact.LinkRevision, waId, contact.ModeRevision);
        conversations.Add(outgoing);
        return outgoing;
    }
}
