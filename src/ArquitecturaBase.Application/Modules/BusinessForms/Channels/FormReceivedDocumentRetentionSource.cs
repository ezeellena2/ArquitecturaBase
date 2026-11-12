using ArquitecturaBase.Application.Interfaces.Channels;
using ArquitecturaBase.Application.Modules.BusinessForms.Interfaces.Persistence;
using ArquitecturaBase.Application.Modules.BusinessForms.Services;

namespace ArquitecturaBase.Application.Modules.BusinessForms.Channels;

/// <summary>
/// Keeps the inbox's copies (<c>forms/documents/*</c>, as <see cref="FormReceivedDocumentCopyPolicy.TryParseKey"/> recognizes them) of the
/// documents still in it, by batch; a copy whose document was purged, or never stored, is left to the cleanup of private files.
/// </summary>
internal sealed class FormReceivedDocumentRetentionSource(IFormReceivedDocumentReader reader) : IPrivateFileRetentionSource
{
    public async Task<IReadOnlyList<string>> ListReferencedKeysAsync(IReadOnlyList<string> storageKeys, CancellationToken cancellationToken)
    {
        var candidates = storageKeys.Where(key => FormReceivedDocumentCopyPolicy.TryParseKey(key, out _)).Distinct(StringComparer.Ordinal).ToArray();
        return candidates.Length == 0 ? [] : await reader.ListLiveStorageKeysAsync(candidates, cancellationToken);
    }
}
