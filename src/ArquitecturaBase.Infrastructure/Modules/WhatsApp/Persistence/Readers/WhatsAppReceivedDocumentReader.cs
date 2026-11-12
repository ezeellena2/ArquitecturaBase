using ArquitecturaBase.Application.Modules.WhatsApp.Configuration;
using ArquitecturaBase.Application.Modules.WhatsApp.Interfaces.Persistence;
using ArquitecturaBase.Application.Modules.WhatsApp.Models;
using ArquitecturaBase.Domain.Modules.WhatsApp;
using ArquitecturaBase.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace ArquitecturaBase.Infrastructure.Modules.WhatsApp.Persistence.Readers;

// Task 8 of the documents: the received documents. One waits within its message's retention: pending to be downloaded, after a failure
// until its next attempt (30, 90, 210, 450 and 930 seconds after the first, as a spreadsheet's), or being read by the engine while its
// content lasts, polled in every round. One that waits for its request is due only when its time passed or a text of the person (or an
// audio once transcribed) followed it; a pending one is taken before a reading, so a slow reading never holds back a reply or a handoff.
internal sealed class WhatsAppReceivedDocumentReader(ApplicationDbContext db, WhatsAppContentRetention retention) : IWhatsAppReceivedDocumentReader
{
    private IQueryable<WhatsAppMessage> Documents() => db.Set<WhatsAppMessage>().AsNoTracking().Where(message => message.Kind == WhatsAppMessageKind.ReceivedDocument
        && message.Direction == WhatsAppMessageDirection.Inbound && message.ContactId != null && message.DocumentState != null);

    private IQueryable<WhatsAppMessage> Waiting(DateTime nowUtc)
    {
        var cutoff = nowUtc.AddDays(-retention.MessageRetentionDays);
        return Documents().Where(message => message.OccurredAtUtc > cutoff && (message.DocumentState == WhatsAppDocumentState.Pending
            || message.DocumentState == WhatsAppDocumentState.Reading && (message.DocumentContentExpiresAtUtc == null || message.DocumentContentExpiresAtUtc > nowUtc)));
    }

    // One that waits for its request is due once its time passed or a text followed it (by the inbound sequence): an audio counts once
    // transcribed, as a spreadsheet waits for it.
    private IQueryable<WhatsAppMessage> Due(DateTime nowUtc) => Waiting(nowUtc).Where(message => message.DocumentState == WhatsAppDocumentState.Reading
        || (message.DocumentRequestAwaitedUntilUtc == null || message.DocumentRequestAwaitedUntilUtc <= nowUtc
                || db.Set<WhatsAppMessage>().Any(next => next.ContactId == message.ContactId && next.Direction == WhatsAppMessageDirection.Inbound
                    && next.InboundSequence > message.InboundSequence && (next.Kind == WhatsAppMessageKind.Text
                        || next.Kind == WhatsAppMessageKind.Audio && next.AudioState == WhatsAppAudioState.Completed)))
            && (message.DocumentFirstFailureAtUtc == null || message.DocumentFirstFailureAtUtc.Value.AddSeconds(
                message.DocumentFailureCount <= 1 ? 30 : message.DocumentFailureCount == 2 ? 90 : message.DocumentFailureCount == 3 ? 210
                    : message.DocumentFailureCount == 4 ? 450 : 930) <= nowUtc));

    private IQueryable<WhatsAppReceivedDocumentSourceRow> Project(IQueryable<WhatsAppMessage> documents) =>
        from message in documents
        join contact in db.Set<WhatsAppContact>().AsNoTracking() on message.ContactId equals contact.Id
        select new WhatsAppReceivedDocumentSourceRow
        {
            MessageId = message.Id, ContactId = contact.Id, OccurredAtUtc = message.OccurredAtUtc, Mode = contact.Mode, LinkedUserId = contact.UserId,
            LinkRevision = contact.LinkRevision, State = message.DocumentState!.Value, Caption = message.Body, MediaId = message.DocumentMediaId,
            ContentType = message.DocumentContentType, Sha256 = message.DocumentSha256, FileName = message.DocumentFileName,
            StorageKey = message.DocumentStorageKey, Size = message.DocumentSize, CopySha256 = message.DocumentCopySha256, Pages = message.DocumentPages,
            HasReading = message.DocumentReadingJson != null, RequestId = message.DocumentRequestId, ActorUserId = message.DocumentActorUserId,
            ContextJson = message.DocumentContextJson, CapturedLinkRevision = message.DocumentLinkRevision, ContentExpiresAtUtc = message.DocumentContentExpiresAtUtc,
            RequestAwaitedUntilUtc = message.DocumentRequestAwaitedUntilUtc,
            FollowedByText = db.Set<WhatsAppMessage>().Any(next => next.ContactId == message.ContactId && next.Direction == WhatsAppMessageDirection.Inbound
                && next.InboundSequence > message.InboundSequence && (next.Kind == WhatsAppMessageKind.Text
                    || next.Kind == WhatsAppMessageKind.Audio && next.AudioState == WhatsAppAudioState.Completed)),
        };

    public async Task<IReadOnlyList<Guid>> ListPendingContactsAsync(IReadOnlyCollection<Guid> excluded, int limit, DateTime nowUtc, CancellationToken ct) =>
        await Due(nowUtc).Select(message => message.ContactId!.Value).Where(id => !excluded.Contains(id))
            .Distinct().OrderBy(id => id).Take(limit).ToListAsync(ct);

    // Only for the processor's wait, as a spreadsheet's: one waiting for its next attempt is still pending; one that waits for its
    // request is not, since nothing hurries it (the next message wakes the processor anyway).
    public Task<bool> ExistsPendingAsync(DateTime nowUtc, CancellationToken ct) => Waiting(nowUtc).Where(message => message.DocumentState == WhatsAppDocumentState.Reading
        || message.DocumentRequestAwaitedUntilUtc == null || message.DocumentRequestAwaitedUntilUtc <= nowUtc).AnyAsync(ct);

    public Task<WhatsAppReceivedDocumentSourceRow?> FindPendingAsync(Guid contactId, DateTime nowUtc, CancellationToken ct) =>
        Project(Due(nowUtc).Where(message => message.ContactId == contactId))
            .OrderBy(row => row.State == WhatsAppDocumentState.Reading).ThenBy(row => row.OccurredAtUtc).ThenBy(row => row.MessageId).FirstOrDefaultAsync(ct);

    public Task<WhatsAppReceivedDocumentSourceRow?> FindAsync(Guid contactId, Guid messageId, CancellationToken ct) =>
        Project(Documents().Where(message => message.ContactId == contactId && message.Id == messageId)).SingleOrDefaultAsync(ct);

    public Task<WhatsAppReceivedDocumentSourceRow?> FindByRequestAsync(Guid contactId, Guid requestId, CancellationToken ct) =>
        Project(Documents().Where(message => message.ContactId == contactId && message.DocumentRequestId == requestId)).SingleOrDefaultAsync(ct);

    // A pending document may have a copy written before its commit; any other keeps one only once it is stored.
    public async Task<IReadOnlyList<Guid>> ListRetainedMessageIdsAsync(IReadOnlyCollection<Guid> messageIds, DateTime cutoffUtc, CancellationToken ct) =>
        await Documents().Where(message => messageIds.Contains(message.Id) && message.OccurredAtUtc > cutoffUtc
                && (message.DocumentState == WhatsAppDocumentState.Pending || message.DocumentStorageKey != null))
            .Select(message => message.Id).ToListAsync(ct);
}
