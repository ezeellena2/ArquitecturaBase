using ArquitecturaBase.Application.Modules.WhatsApp.Models;

namespace ArquitecturaBase.Application.Modules.WhatsApp.Interfaces.Persistence;

/// <summary>The received spreadsheets: the pending ones whose next attempt is due, one by its id, and the copies still kept.</summary>
public interface IWhatsAppSpreadsheetReader
{
    Task<IReadOnlyList<Guid>> ListPendingContactsAsync(IReadOnlyCollection<Guid> excluded, int limit, DateTime nowUtc, CancellationToken ct);
    Task<WhatsAppSpreadsheetSourceRow?> FindPendingAsync(Guid contactId, DateTime nowUtc, CancellationToken ct);
    Task<WhatsAppSpreadsheetSourceRow?> FindAsync(Guid contactId, Guid messageId, CancellationToken ct);

    /// <summary>Of these messages, the ones whose spreadsheet may have a copy to keep: pending or stored, received after the cutoff.</summary>
    Task<IReadOnlyList<Guid>> ListRetainedMessageIdsAsync(IReadOnlyCollection<Guid> messageIds, DateTime cutoffUtc, CancellationToken ct);
}
