using ArquitecturaBase.Application.Modules.WhatsApp.Configuration;
using ArquitecturaBase.Application.Modules.WhatsApp.Interfaces.Persistence;
using ArquitecturaBase.Application.Modules.WhatsApp.Models;
using ArquitecturaBase.Domain.Modules.WhatsApp;
using ArquitecturaBase.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace ArquitecturaBase.Infrastructure.Modules.WhatsApp.Persistence.Readers;

// Stage 2 of the assistant authoring: the received spreadsheets. A pending one waits within its message's retention and, after a
// failure, until its next attempt (30, 90, 210, 450 and 930 seconds after the first, as an audio's download).
internal sealed class WhatsAppSpreadsheetReader(ApplicationDbContext db, WhatsAppContentRetention retention) : IWhatsAppSpreadsheetReader
{
    private IQueryable<WhatsAppMessage> Spreadsheets() => db.Set<WhatsAppMessage>().AsNoTracking().Where(message => message.Kind == WhatsAppMessageKind.Spreadsheet
        && message.Direction == WhatsAppMessageDirection.Inbound && message.ContactId != null && message.SpreadsheetState != null);

    private IQueryable<WhatsAppMessage> Pending(DateTime nowUtc)
    {
        var cutoff = nowUtc.AddDays(-retention.MessageRetentionDays);
        return Spreadsheets().Where(message => message.SpreadsheetState == WhatsAppSpreadsheetState.Pending && message.OccurredAtUtc > cutoff
            && (message.SpreadsheetFirstFailureAtUtc == null || message.SpreadsheetFirstFailureAtUtc.Value.AddSeconds(
                message.SpreadsheetFailureCount <= 1 ? 30 : message.SpreadsheetFailureCount == 2 ? 90 : message.SpreadsheetFailureCount == 3 ? 210
                    : message.SpreadsheetFailureCount == 4 ? 450 : 930) <= nowUtc));
    }

    private IQueryable<WhatsAppSpreadsheetSourceRow> Project(IQueryable<WhatsAppMessage> spreadsheets) =>
        from message in spreadsheets
        join contact in db.Set<WhatsAppContact>().AsNoTracking() on message.ContactId equals contact.Id
        select new WhatsAppSpreadsheetSourceRow
        {
            MessageId = message.Id, ContactId = contact.Id, OccurredAtUtc = message.OccurredAtUtc, Mode = contact.Mode, LinkedUserId = contact.UserId,
            LinkRevision = contact.LinkRevision, State = message.SpreadsheetState!.Value, MediaId = message.SpreadsheetMediaId,
            ContentType = message.SpreadsheetContentType, Sha256 = message.SpreadsheetSha256, FileName = message.SpreadsheetFileName,
        };

    public async Task<IReadOnlyList<Guid>> ListPendingContactsAsync(IReadOnlyCollection<Guid> excluded, int limit, DateTime nowUtc, CancellationToken ct) =>
        await Pending(nowUtc).Select(message => message.ContactId!.Value).Where(id => !excluded.Contains(id))
            .Distinct().OrderBy(id => id).Take(limit).ToListAsync(ct);

    // Only for the processor's wait, as an audio's: one waiting for its next attempt is still pending.
    public Task<bool> ExistsPendingAsync(DateTime nowUtc, CancellationToken ct) => Spreadsheets().AnyAsync(message =>
        message.SpreadsheetState == WhatsAppSpreadsheetState.Pending && message.OccurredAtUtc > nowUtc.AddDays(-retention.MessageRetentionDays), ct);

    public Task<WhatsAppSpreadsheetSourceRow?> FindPendingAsync(Guid contactId, DateTime nowUtc, CancellationToken ct) =>
        Project(Pending(nowUtc).Where(message => message.ContactId == contactId))
            .OrderBy(row => row.OccurredAtUtc).ThenBy(row => row.MessageId).FirstOrDefaultAsync(ct);

    public Task<WhatsAppSpreadsheetSourceRow?> FindAsync(Guid contactId, Guid messageId, CancellationToken ct) =>
        Project(Spreadsheets().Where(message => message.ContactId == contactId && message.Id == messageId)).SingleOrDefaultAsync(ct);

    public async Task<IReadOnlyList<Guid>> ListRetainedMessageIdsAsync(IReadOnlyCollection<Guid> messageIds, DateTime cutoffUtc, CancellationToken ct) =>
        await Spreadsheets().Where(message => messageIds.Contains(message.Id) && message.OccurredAtUtc > cutoffUtc
                && (message.SpreadsheetState == WhatsAppSpreadsheetState.Pending || message.SpreadsheetState == WhatsAppSpreadsheetState.Stored))
            .Select(message => message.Id).ToListAsync(ct);
}
