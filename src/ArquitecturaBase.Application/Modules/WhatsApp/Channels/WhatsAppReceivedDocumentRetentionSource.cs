using ArquitecturaBase.Application.Interfaces.Channels;
using ArquitecturaBase.Application.Modules.WhatsApp.Configuration;
using ArquitecturaBase.Application.Modules.WhatsApp.Interfaces.Persistence;
using ArquitecturaBase.Application.Modules.WhatsApp.Services;

namespace ArquitecturaBase.Application.Modules.WhatsApp.Channels;

// Task 8 of the documents: the copy of a received document (a PDF, a photo or a Word; the prefix is the spreadsheets', the extension
// tells them apart) stays while its message keeps its content, also the one written before the commit: its key names the message.
internal sealed class WhatsAppReceivedDocumentRetentionSource(IWhatsAppReceivedDocumentReader reader, WhatsAppContentRetention retention, TimeProvider clock)
    : IPrivateFileRetentionSource
{
    public async Task<IReadOnlyList<string>> ListReferencedKeysAsync(IReadOnlyList<string> storageKeys, CancellationToken cancellationToken)
    {
        var candidates = new Dictionary<string, Guid>(StringComparer.Ordinal);
        foreach (var key in storageKeys)
            if (WhatsAppReceivedDocumentPreparationPolicy.TryParseKey(key, out var messageId)) candidates[key] = messageId;
        if (candidates.Count == 0) return [];
        var retained = (await reader.ListRetainedMessageIdsAsync([.. candidates.Values.Distinct()],
            clock.GetUtcNow().UtcDateTime.AddDays(-retention.MessageRetentionDays), cancellationToken)).ToHashSet();
        return [.. candidates.Where(candidate => retained.Contains(candidate.Value)).Select(candidate => candidate.Key)];
    }
}
