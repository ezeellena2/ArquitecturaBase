using ArquitecturaBase.Application.Modules.WhatsApp.Models;
using ArquitecturaBase.Domain.Modules.WhatsApp;

namespace ArquitecturaBase.Application.Modules.WhatsApp.Services;

internal static class WhatsAppInboundPolicy
{
    public static (WhatsAppMessagePurpose Purpose, WhatsAppAccessEventKind? EventKind) Classify(WhatsAppMessageKind kind, string? replyId) =>
        kind == WhatsAppMessageKind.ButtonReply && WhatsAppAccessActions.IsKnown(replyId)
            ? (WhatsAppMessagePurpose.Access, WhatsAppAccessEventKind.AccessRequested)
            : kind == WhatsAppMessageKind.System
                ? (WhatsAppMessagePurpose.Unclassified, null)
                : (WhatsAppMessagePurpose.Business, null);

    public static WhatsAppMessage? SelectAction(IReadOnlyList<WhatsAppMessage> pending, DateTime nowUtc) =>
        pending.Where(message => message.OccurredAtUtc > nowUtc - WhatsAppInboundService.ReplyWindow)
            .Where(message => Classify(message.Kind, message.ReplyId).Purpose == WhatsAppMessagePurpose.Access)
            .Where(message => message.ReplyId != WhatsAppAccessActions.DidNotRequestCode)
            .OrderByDescending(message => message.OccurredAtUtc).ThenByDescending(message => message.Id).FirstOrDefault();
}
