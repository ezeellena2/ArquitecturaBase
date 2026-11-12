using ArquitecturaBase.Application.Modules.WhatsApp.Models;

namespace ArquitecturaBase.Application.Modules.WhatsApp.Interfaces.Persistence;

/// <summary>
/// The received documents (task 8 of the documents): the contacts with one that waits, its next attempt due (pending to be downloaded,
/// or being read by the engine), one by its id or by its reading's request, and the copies still kept.
/// </summary>
public interface IWhatsAppReceivedDocumentReader
{
    Task<IReadOnlyList<Guid>> ListPendingContactsAsync(IReadOnlyCollection<Guid> excluded, int limit, DateTime nowUtc, CancellationToken ct);

    /// <summary>Whether a document still waits, also while it waits for its next attempt: the processor polls sooner.</summary>
    Task<bool> ExistsPendingAsync(DateTime nowUtc, CancellationToken ct);

    /// <summary>The contact's oldest document that waits and is due: pending to be downloaded, or being read.</summary>
    Task<WhatsAppReceivedDocumentSourceRow?> FindPendingAsync(Guid contactId, DateTime nowUtc, CancellationToken ct);
    Task<WhatsAppReceivedDocumentSourceRow?> FindAsync(Guid contactId, Guid messageId, CancellationToken ct);
    Task<WhatsAppReceivedDocumentSourceRow?> FindByRequestAsync(Guid contactId, Guid requestId, CancellationToken ct);

    /// <summary>Of these messages, the ones whose document may have a copy to keep: pending or stored with one, received after the cutoff.</summary>
    Task<IReadOnlyList<Guid>> ListRetainedMessageIdsAsync(IReadOnlyCollection<Guid> messageIds, DateTime cutoffUtc, CancellationToken ct);
}
