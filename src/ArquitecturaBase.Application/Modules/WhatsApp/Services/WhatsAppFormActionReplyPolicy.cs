using System.Text.Json;
using ArquitecturaBase.Application.Modules.WhatsApp.Models;
using ArquitecturaBase.Domain.Modules.WhatsApp;

namespace ArquitecturaBase.Application.Modules.WhatsApp.Services;

/// <summary>
/// Maps a tap back to its form action without trusting the sender: the button must be one the row sent, the tap must come from
/// the row's contact with the same link and number, and the recipient must still be reachable (active account and confirmed
/// number equal to the chat's). Anything else is ignored. It reads; it never saves.
/// </summary>
internal sealed class WhatsAppFormActionReplyPolicy(WhatsAppFormActionPolicy actions)
{
    public async Task<WhatsAppFormActionReply?> ResolveAsync(WhatsAppFormActionTapRow tap, WhatsAppFormActionReplyTargetRow? target,
        CancellationToken cancellationToken)
    {
        if (target is null || !WhatsAppFormActionChoice.TryParseReplyId(tap.ReplyId, out var outgoingId, out var choiceId)
            || outgoingId != target.OutgoingId || tap.ContactId != target.ContactId || tap.ContactUserId != target.RecipientUserId
            || tap.ContactLinkRevision != target.RecipientLinkRevision || tap.ContactWaId is null || tap.ContactWaId != target.RecipientWaId
            || !SentIds(target).Contains(tap.ReplyId, StringComparer.Ordinal))
            return null;
        return await actions.CanReachAsync(tap.ContactWaId, target.RecipientUserId, cancellationToken)
            ? new WhatsAppFormActionReply(target.RunId, target.RecipientUserId, choiceId) : null;
    }

    private static IEnumerable<string> SentIds(WhatsAppFormActionReplyTargetRow target) =>
        JsonSerializer.Deserialize<WhatsAppOutgoingQuickReply[]>(target.QuickRepliesJson)?.Select(reply => reply.Id) ?? [];
}
