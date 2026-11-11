using ArquitecturaBase.Application.Models.StructuredData;
using ArquitecturaBase.Application.Modules.WhatsApp.Interfaces.Persistence;
using ArquitecturaBase.Application.Modules.WhatsApp.Models;
using ArquitecturaBase.Domain.Modules.WhatsApp;
using ArquitecturaBase.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
namespace ArquitecturaBase.Infrastructure.Modules.WhatsApp.Persistence.Readers;

internal sealed class WhatsAppFormActionReader(ApplicationDbContext db) : IWhatsAppFormActionReader
{
    public async Task<IReadOnlyList<WhatsAppFormActionObservationRow>> ListObservationsAsync(IReadOnlyCollection<StructuredRecordActionObservationRequest> requests, CancellationToken cancellationToken)
    {
        if (requests.Count == 0) return [];
        var requested = requests.ToHashSet(); var runs = requested.Select(request => request.RunId).Distinct().ToArray(); var users = requested.Select(request => request.RecipientUserId).Distinct().ToArray();
        var rows = await QueryObservations(runs, users).ToArrayAsync(cancellationToken);
        return rows.Where(row => requested.Contains(new(row.RunId, row.RecipientUserId))).ToArray();
    }
    public Task<string?> FindLinkedWaIdAsync(Guid userId, CancellationToken cancellationToken) =>
        db.Set<WhatsAppContact>().AsNoTracking().Where(contact => contact.UserId == userId).Select(contact => contact.WaId).SingleOrDefaultAsync(cancellationToken);
    public async Task<IReadOnlyList<WhatsAppFormActionTapRow>> ListPendingRepliesAsync(Guid contactId, DateTime nowUtc, int limit, CancellationToken cancellationToken) =>
        await (from message in PendingReplies(nowUtc)
               join contact in db.Set<WhatsAppContact>().AsNoTracking() on message.ContactId equals contact.Id
               where contact.Id == contactId
               orderby message.InboundSequence, message.Id
               select new WhatsAppFormActionTapRow(message.Id, message.WaMessageId, contact.Id, message.ReplyId!, contact.UserId, contact.LinkRevision, contact.WaId))
            .Take(limit).ToArrayAsync(cancellationToken);
    // From the few form actions sent with buttons, not from every message: the scan stays small without another index.
    public async Task<IReadOnlyList<Guid>> ListContactsWithPendingRepliesAsync(IReadOnlyCollection<Guid> excluded, int limit, DateTime nowUtc, CancellationToken cancellationToken)
    {
        var pending = PendingReplies(nowUtc);
        return await db.Set<WhatsAppOutgoingMessage>().AsNoTracking()
            .Where(outgoing => outgoing.Origin == WhatsAppMessageOrigin.FormAction && outgoing.Kind == WhatsAppOutgoingMessageKind.QuickReplies)
            .Select(outgoing => outgoing.ContactId).Distinct()
            .Where(contactId => !excluded.Contains(contactId) && pending.Any(message => message.ContactId == contactId))
            .OrderBy(contactId => contactId).Take(limit).ToListAsync(cancellationToken);
    }
    public Task<WhatsAppFormActionReplyTargetRow?> FindReplyTargetAsync(Guid outgoingId, CancellationToken cancellationToken) =>
        db.Set<WhatsAppOutgoingMessage>().AsNoTracking()
            .Where(outgoing => outgoing.Id == outgoingId && outgoing.Origin == WhatsAppMessageOrigin.FormAction
                && outgoing.Kind == WhatsAppOutgoingMessageKind.QuickReplies && outgoing.RecipientUserId != null && outgoing.QuickRepliesJson != null)
            .Select(outgoing => new WhatsAppFormActionReplyTargetRow(outgoing.Id, outgoing.ContactId, outgoing.RequestId, outgoing.RecipientUserId!.Value,
                outgoing.RecipientLinkRevision, outgoing.RecipientWaId, outgoing.QuickRepliesJson!))
            .SingleOrDefaultAsync(cancellationToken);
    private IQueryable<WhatsAppMessage> PendingReplies(DateTime nowUtc)
    {
        var sinceUtc = nowUtc - WhatsAppFormActionChoice.PendingReplyWindow;
        return db.Set<WhatsAppMessage>().AsNoTracking().Where(message => message.Direction == WhatsAppMessageDirection.Inbound
            && message.Kind == WhatsAppMessageKind.ButtonReply && message.ProcessedAtUtc != null && message.AssistantProcessedAtUtc == null
            && message.OccurredAtUtc > sinceUtc && message.ReplyId != null && EF.Functions.Like(message.ReplyId, WhatsAppFormActionChoice.ReplyPrefix + "%"));
    }
    private IQueryable<WhatsAppFormActionObservationRow> QueryObservations(IReadOnlyCollection<Guid> runIds, IReadOnlyCollection<Guid> userIds) =>
        from outgoing in db.Set<WhatsAppOutgoingMessage>().AsNoTracking()
        where outgoing.Origin == WhatsAppMessageOrigin.FormAction && runIds.Contains(outgoing.RequestId)
            && outgoing.RecipientUserId != null && userIds.Contains(outgoing.RecipientUserId.Value)
        join receipt in db.Set<WhatsAppDeliveryReceipt>().AsNoTracking() on outgoing.WaMessageId equals receipt.WaMessageId into receipts
        from receipt in receipts.DefaultIfEmpty()
        select new WhatsAppFormActionObservationRow(outgoing.RequestId, outgoing.RecipientUserId!.Value, outgoing.State, outgoing.FailureCode,
            !string.IsNullOrWhiteSpace(outgoing.Body), outgoing.LastFormActionControlRevision, outgoing.DeliveryReviewWasSent, outgoing.DeliveryReviewedByUserId, outgoing.DeliveryReviewedAtUtc,
            receipt == null ? null : receipt.DeliveredAtUtc ?? (receipt.Status == WhatsAppMessageStatus.Delivered ? (DateTime?)receipt.OccurredAtUtc : null),
            receipt == null ? null : receipt.ReadAtUtc ?? (receipt.Status == WhatsAppMessageStatus.Read ? (DateTime?)receipt.OccurredAtUtc : null));
}
