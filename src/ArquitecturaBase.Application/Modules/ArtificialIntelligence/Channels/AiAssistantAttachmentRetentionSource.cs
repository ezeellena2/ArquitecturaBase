using ArquitecturaBase.Application.Interfaces.Channels;
using ArquitecturaBase.Application.Modules.ArtificialIntelligence.Interfaces.Persistence;
using ArquitecturaBase.Application.Modules.ArtificialIntelligence.Services;

namespace ArquitecturaBase.Application.Modules.ArtificialIntelligence.Channels;

// Stage 2: a Home spreadsheet stays while a turn that has it, its own or inherited, keeps its content; its key names its request.
internal sealed class AiAssistantAttachmentRetentionSource(IAiAssistantReader reader) : IPrivateFileRetentionSource
{
    public async Task<IReadOnlyList<string>> ListReferencedKeysAsync(IReadOnlyList<string> storageKeys, CancellationToken cancellationToken)
    {
        var candidates = storageKeys.Where(key => AiAssistantHomeAttachmentPolicy.TryParseKey(key, out _)).Distinct(StringComparer.Ordinal).ToArray();
        if (candidates.Length == 0) return [];
        return await reader.ListRetainedAttachmentKeysAsync(candidates, cancellationToken);
    }
}
