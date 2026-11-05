using ArquitecturaBase.Application.Interfaces.Channels;
using ArquitecturaBase.Application.Modules.ArtificialIntelligence.Interfaces.Persistence;
using ArquitecturaBase.Application.Modules.ArtificialIntelligence.Services;

namespace ArquitecturaBase.Application.Modules.ArtificialIntelligence.Channels;

// IA4: a Home recording stays while the turn that names it keeps its content; its key carries the request that normalized it.
internal sealed class AiAssistantAudioRetentionSource(IAiAssistantReader reader) : IPrivateFileRetentionSource
{
    public async Task<IReadOnlyList<string>> ListReferencedKeysAsync(IReadOnlyList<string> storageKeys, CancellationToken cancellationToken)
    {
        var candidates = new Dictionary<string, Guid>(StringComparer.Ordinal);
        foreach (var key in storageKeys)
            if (AiAssistantHomeAudioPolicy.TryParseKey(key, out var requestId)) candidates.TryAdd(key, requestId);
        if (candidates.Count == 0) return [];
        var retained = (await reader.ListRetainedAudioRequestIdsAsync(candidates.Values.Distinct().ToArray(), cancellationToken)).ToHashSet();
        return candidates.Where(candidate => retained.Contains(candidate.Value)).Select(candidate => candidate.Key).ToArray();
    }
}
