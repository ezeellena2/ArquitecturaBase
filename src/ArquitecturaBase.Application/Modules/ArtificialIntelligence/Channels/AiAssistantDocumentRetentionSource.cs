using ArquitecturaBase.Application.Interfaces.Channels;
using ArquitecturaBase.Application.Modules.ArtificialIntelligence.Interfaces.Persistence;
using ArquitecturaBase.Application.Modules.ArtificialIntelligence.Services;

namespace ArquitecturaBase.Application.Modules.ArtificialIntelligence.Channels;

// Stage 3: a Home photo or PDF stays while a turn can still take its reading (pending, running or completed, with its content), or while
// a turn that holds the copy, its own or inherited, keeps its content; a failed, uncertain, canceled or expired reading releases it. Its
// key names the reading's request.
internal sealed class AiAssistantDocumentRetentionSource(IAiAssistantReader reader) : IPrivateFileRetentionSource
{
    public async Task<IReadOnlyList<string>> ListReferencedKeysAsync(IReadOnlyList<string> storageKeys, CancellationToken cancellationToken)
    {
        var candidates = new Dictionary<string, Guid>(StringComparer.Ordinal);
        foreach (var key in storageKeys)
            if (AiAssistantHomeDocumentPolicy.TryParseKey(key, out var requestId)) candidates.TryAdd(key, requestId);
        if (candidates.Count == 0) return [];
        var reading = (await reader.ListRetainedRequestIdsAsync(AiAssistantChannelPolicy.HomeDocumentChannelId, [.. candidates.Values.Distinct()],
            cancellationToken)).ToHashSet();
        var held = (await reader.ListRetainedAttachmentKeysAsync([.. candidates.Keys], cancellationToken)).ToHashSet(StringComparer.Ordinal);
        return [.. candidates.Where(candidate => reading.Contains(candidate.Value) || held.Contains(candidate.Key)).Select(candidate => candidate.Key)];
    }
}
