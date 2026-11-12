using ArquitecturaBase.Application.Modules.WhatsApp.Interfaces.Persistence;
using ArquitecturaBase.Application.Modules.WhatsApp.Models;
using ArquitecturaBase.Domain.Modules.WhatsApp;
using ArquitecturaBase.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace ArquitecturaBase.Infrastructure.Modules.WhatsApp.Persistence.Readers;

internal sealed class WhatsAppAssistantHistoryReader(ApplicationDbContext db) : IWhatsAppAssistantHistoryReader
{
    public Task<Guid?> FindConversationAsync(Guid subjectUserId, Guid? contactId, CancellationToken cancellationToken) =>
        db.Set<WhatsAppContact>().AsNoTracking()
            .Where(contact => contact.UserId == subjectUserId && (contactId == null || contact.Id == contactId))
            .Select(contact => (Guid?)contact.Id).SingleOrDefaultAsync(cancellationToken);

    public async Task<IReadOnlyList<WhatsAppAssistantHistoryMessageRow>> ListMessagesAsync(Guid contactId, DateTime sinceUtc, int limit,
        CancellationToken cancellationToken)
    {
        var rows = await db.Set<WhatsAppMessage>().AsNoTracking()
            .Where(message => message.ContactId == contactId && message.OccurredAtUtc >= sinceUtc && message.Purpose != WhatsAppMessagePurpose.Access
                && message.Body != null && message.Body != "")
            .OrderByDescending(message => message.OccurredAtUtc).ThenByDescending(message => message.Id).Take(limit)
            .Select(message => new WhatsAppAssistantHistoryMessageRow(message.OccurredAtUtc, message.Direction, message.Origin, message.Kind, message.Body))
            .ToListAsync(cancellationToken);
        rows.Reverse();
        return rows;
    }

    public async Task<DateTime?> FindBurstStartAsync(Guid contactId, CancellationToken cancellationToken)
    {
        var sentUtc = await db.Set<WhatsAppOutgoingMessage>().AsNoTracking().Where(outgoing => outgoing.ContactId == contactId)
            .MaxAsync(outgoing => (DateTime?)outgoing.RequestedAtUtc, cancellationToken) ?? DateTime.MinValue;
        var answeredUtc = await db.Set<WhatsAppMessage>().AsNoTracking()
            .Where(message => message.ContactId == contactId && message.Direction == WhatsAppMessageDirection.Inbound && message.AssistantProcessedAtUtc != null)
            .MaxAsync(message => (DateTime?)message.OccurredAtUtc, cancellationToken) ?? DateTime.MinValue;
        var afterUtc = sentUtc > answeredUtc ? sentUtc : answeredUtc;
        return await db.Set<WhatsAppMessage>().AsNoTracking()
            .Where(message => message.ContactId == contactId && message.Direction == WhatsAppMessageDirection.Inbound
                && message.Purpose == WhatsAppMessagePurpose.Business && message.OccurredAtUtc > afterUtc)
            .MinAsync(message => (DateTime?)message.OccurredAtUtc, cancellationToken);
    }

    public async Task<IReadOnlyList<WhatsAppAssistantHistoryActionRow>> ListActionsAsync(Guid contactId, DateTime sinceUtc, int limit,
        CancellationToken cancellationToken)
    {
        var rows = await db.Set<WhatsAppOutgoingMessage>().AsNoTracking()
            .Where(outgoing => outgoing.ContactId == contactId && outgoing.Origin == WhatsAppMessageOrigin.Assistant && outgoing.RequestedAtUtc >= sinceUtc
                && outgoing.State != WhatsAppOutgoingMessageState.Failed)
            .OrderByDescending(outgoing => outgoing.RequestedAtUtc).ThenByDescending(outgoing => outgoing.Id).Take(limit)
            .Select(outgoing => new WhatsAppAssistantHistoryActionRow(outgoing.RequestedAtUtc, outgoing.Kind, outgoing.State,
                outgoing.FlowSession != null ? outgoing.FlowSession.NameJson : null, outgoing.FlowSession != null ? outgoing.FlowSession.Culture : null,
                outgoing.FlowSession != null ? outgoing.FlowSession.CaptureState : null, outgoing.FlowSession != null ? outgoing.FlowSession.ExpiresAtUtc : null,
                outgoing.IsAssistantHandoff, outgoing.AssistantReplyJson))
            .ToListAsync(cancellationToken);
        rows.Reverse();
        return rows;
    }

    public Task<int> CountRepliesAsync(Guid contactId, DateTime sinceUtc, CancellationToken cancellationToken) =>
        db.Set<WhatsAppOutgoingMessage>().AsNoTracking().CountAsync(outgoing => outgoing.ContactId == contactId
            && outgoing.Origin == WhatsAppMessageOrigin.Assistant && outgoing.RequestedAtUtc >= sinceUtc && !outgoing.IsAssistantHandoff
            && outgoing.AssistantReplyJson != null && outgoing.Kind != WhatsAppOutgoingMessageKind.Flow
            && outgoing.State != WhatsAppOutgoingMessageState.Failed, cancellationToken);
}
