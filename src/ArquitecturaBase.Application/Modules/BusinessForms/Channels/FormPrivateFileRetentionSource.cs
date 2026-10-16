using ArquitecturaBase.Application.Interfaces.Channels;
using ArquitecturaBase.Application.Modules.BusinessForms.Interfaces.Persistence;
using ArquitecturaBase.Application.Modules.BusinessForms.Services;
namespace ArquitecturaBase.Application.Modules.BusinessForms.Channels;
internal sealed class FormPrivateFileRetentionSource(IFormActionRunReader reader) : IPrivateFileRetentionSource
{
    public async Task<IReadOnlyList<string>> ListReferencedKeysAsync(IReadOnlyList<string> storageKeys, CancellationToken cancellationToken)
    {
        var candidates = new Dictionary<string, Guid>(StringComparer.Ordinal);
        foreach (var key in storageKeys)
            if (FormPdfStorageKey.TryParse(key, out var runId, out _)) candidates.TryAdd(key, runId);
        if (candidates.Count == 0) return [];
        var existing = (await reader.ListExistingPdfRunIdsAsync(candidates.Values.Distinct().ToArray(), cancellationToken)).ToHashSet();
        return candidates.Where(candidate => existing.Contains(candidate.Value)).Select(candidate => candidate.Key).ToArray();
    }
}
