using ArquitecturaBase.Application.Modules.WhatsApp.Models;

namespace ArquitecturaBase.Application.Modules.WhatsApp.Interfaces.Persistence;

public interface IWhatsAppAudioReader
{
    Task<IReadOnlyList<Guid>> ListPendingContactsAsync(IReadOnlyCollection<Guid> excluded, int limit, DateTime nowUtc, CancellationToken ct);
    Task<bool> ExistsPendingAsync(DateTime nowUtc, CancellationToken ct);
    Task<WhatsAppAudioSourceRow?> FindPendingAsync(Guid contactId, DateTime nowUtc, CancellationToken ct);
    Task<WhatsAppAudioSourceRow?> FindAsync(Guid contactId, Guid messageId, CancellationToken ct);
    Task<IReadOnlyList<string>> ListReferencedKeysAsync(IReadOnlyList<string> storageKeys, DateTime cutoffUtc, CancellationToken ct);
}
