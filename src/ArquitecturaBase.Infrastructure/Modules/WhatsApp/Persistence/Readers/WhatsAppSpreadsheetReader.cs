using ArquitecturaBase.Application.Modules.WhatsApp.Interfaces.Persistence;
using ArquitecturaBase.Application.Modules.WhatsApp.Models;
using ArquitecturaBase.Domain.Modules.WhatsApp;
using ArquitecturaBase.Infrastructure.Persistence;
using System.Linq.Expressions;
using Microsoft.EntityFrameworkCore;

namespace ArquitecturaBase.Infrastructure.Modules.WhatsApp.Persistence.Readers;

// Stage 2 of the assistant authoring: the received spreadsheets. A pending one waits within its message's retention and, after a
// failure, until its next attempt (30, 90, 210, 450, 930, 1890 and 3810 seconds and then an hour more each time (H11) after the first, as an audio's download).
internal sealed class WhatsAppSpreadsheetReader(ApplicationDbContext db, IWhatsAppSettingsReader settings) : IWhatsAppSpreadsheetReader
{
    // RC3-A: the retention of Configuración › WhatsApp, read on every use (90 days until it is saved).
    private async Task<int> RetentionDaysAsync(CancellationToken ct) => (await settings.FindAsync(ct)).MessageRetentionDays;

    private IQueryable<WhatsAppMessage> Spreadsheets() => db.Set<WhatsAppMessage>().AsNoTracking().Where(message => message.Kind == WhatsAppMessageKind.Spreadsheet
        && message.Direction == WhatsAppMessageDirection.Inbound && message.ContactId != null && message.SpreadsheetState != null);

    private IQueryable<WhatsAppMessage> Pending(DateTime nowUtc, int days)
    {
        var cutoff = nowUtc.AddDays(-days);
        return Spreadsheets().Where(message => message.SpreadsheetState == WhatsAppSpreadsheetState.Pending && message.OccurredAtUtc > cutoff)
            .Where(RetryDueAt(nowUtc));
    }

    // The audio's scale (H11: an hour more for each failure past the seventh).
    internal static Expression<Func<WhatsAppMessage, bool>> RetryDueAt(DateTime nowUtc) => message =>
        message.SpreadsheetFirstFailureAtUtc == null || message.SpreadsheetFirstFailureAtUtc.Value.AddSeconds(
            message.SpreadsheetFailureCount <= 1 ? 30 : message.SpreadsheetFailureCount == 2 ? 90 : message.SpreadsheetFailureCount == 3 ? 210
                : message.SpreadsheetFailureCount == 4 ? 450 : message.SpreadsheetFailureCount == 5 ? 930 : message.SpreadsheetFailureCount == 6 ? 1890 : message.SpreadsheetFailureCount == 7 ? 3810 : 3810 + (message.SpreadsheetFailureCount - 7) * 3600) <= nowUtc;

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
        await Pending(nowUtc, await RetentionDaysAsync(ct)).Select(message => message.ContactId!.Value).Where(id => !excluded.Contains(id))
            .Distinct().OrderBy(id => id).Take(limit).ToListAsync(ct);

    // Only for the processor's wait, as an audio's: one waiting for its next attempt counts once it is near (H11).
    public async Task<bool> ExistsPendingAsync(DateTime nowUtc, CancellationToken ct)
    {
        var cutoff = nowUtc.AddDays(-await RetentionDaysAsync(ct));
        return await Spreadsheets().Where(message =>
        message.SpreadsheetState == WhatsAppSpreadsheetState.Pending && message.OccurredAtUtc > cutoff)
        .Where(RetryDueAt(nowUtc + WhatsAppAcquisitionRetry.ImminentAttempt)).AnyAsync(ct);
    }

    public async Task<WhatsAppSpreadsheetSourceRow?> FindPendingAsync(Guid contactId, DateTime nowUtc, CancellationToken ct) =>
        await Project(Pending(nowUtc, await RetentionDaysAsync(ct)).Where(message => message.ContactId == contactId))
            .OrderBy(row => row.OccurredAtUtc).ThenBy(row => row.MessageId).FirstOrDefaultAsync(ct);

    public Task<WhatsAppSpreadsheetSourceRow?> FindAsync(Guid contactId, Guid messageId, CancellationToken ct) =>
        Project(Spreadsheets().Where(message => message.ContactId == contactId && message.Id == messageId)).SingleOrDefaultAsync(ct);

    public async Task<IReadOnlyList<Guid>> ListRetainedMessageIdsAsync(IReadOnlyCollection<Guid> messageIds, DateTime cutoffUtc, CancellationToken ct) =>
        await Spreadsheets().Where(message => messageIds.Contains(message.Id) && message.OccurredAtUtc > cutoffUtc
                && (message.SpreadsheetState == WhatsAppSpreadsheetState.Pending || message.SpreadsheetState == WhatsAppSpreadsheetState.Stored))
            .Select(message => message.Id).ToListAsync(ct);
}
