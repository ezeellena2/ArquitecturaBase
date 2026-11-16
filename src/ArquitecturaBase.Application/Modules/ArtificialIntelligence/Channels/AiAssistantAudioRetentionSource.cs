using ArquitecturaBase.Application.Interfaces.Channels;
using ArquitecturaBase.Application.Modules.ArtificialIntelligence.Interfaces.Persistence;
using ArquitecturaBase.Application.Modules.ArtificialIntelligence.Services;

namespace ArquitecturaBase.Application.Modules.ArtificialIntelligence.Channels;

// IA4: a Home recording stays while its normalization and a turn that names it keep their content; its key carries the request that
// normalized it. Only the normalization lets its person listen to it, so once that one is purged (I2: its conversation was removed) the
// copy goes, although another conversation's turn still names it with its own sealed transcript.
internal sealed class AiAssistantAudioRetentionSource(IAiAssistantReader reader) : IPrivateFileRetentionSource
{
    public async Task<IReadOnlyList<string>> ListReferencedKeysAsync(IReadOnlyList<string> storageKeys, CancellationToken cancellationToken)
    {
        var candidates = new Dictionary<string, Guid>(StringComparer.Ordinal);
        foreach (var key in storageKeys)
            if (AiAssistantHomeAudioPolicy.TryParseKey(key, out var requestId)) candidates.TryAdd(key, requestId);
        if (candidates.Count == 0) return [];
        var heard = await reader.ListRetainedRequestIdsAsync(AiAssistantChannelPolicy.HomeAudioChannelId, [.. candidates.Values.Distinct()], cancellationToken);
        if (heard.Count == 0) return [];
        var retained = (await reader.ListRetainedAudioRequestIdsAsync(heard, cancellationToken)).ToHashSet();
        return candidates.Where(candidate => retained.Contains(candidate.Value)).Select(candidate => candidate.Key).ToArray();
    }
}
