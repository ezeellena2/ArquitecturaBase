using System.Security.Cryptography;
using System.Text.Json;
using ArquitecturaBase.Application.Interfaces.Integrations.Files;
using ArquitecturaBase.Application.Models.Assistant;
using ArquitecturaBase.Application.Modules.ArtificialIntelligence.Interfaces.Integrations;
using ArquitecturaBase.Application.Modules.ArtificialIntelligence.Interfaces.Persistence;
using ArquitecturaBase.Application.Modules.ArtificialIntelligence.Models;
using ArquitecturaBase.Domain.Files;
using ArquitecturaBase.Domain.Modules.ArtificialIntelligence;
using ArquitecturaBase.Domain.Results;

namespace ArquitecturaBase.Application.Modules.ArtificialIntelligence.Services;

// IA4: the private copy of a Home recording lives under a key the server derives from its request, so the retention source can
// keep it while a turn that heard it keeps its content. A second host may win WriteOnce: the copy is re-read, never assumed ours.
internal sealed class AiAssistantHomeAudioPolicy(IPrivateFileStore files, IAiAssistantRepository repository, IAiConversationProtector protector,
    TimeProvider clock)
{
    private const string Prefix = "ai/assistant/audio/";

    public static string Key(Guid requestId, string mediaType) => $"{Prefix}{requestId:N}{AiAssistantAudioPolicy.Extension(mediaType)}";

    public static bool TryParseKey(string key, out Guid requestId)
    {
        requestId = Guid.Empty;
        if (!key.StartsWith(Prefix, StringComparison.Ordinal) || key.Length < Prefix.Length + 32) return false;
        var name = key[Prefix.Length..];
        return AiAssistantAudioPolicy.MediaTypes.Any(type => name == name[..32] + AiAssistantAudioPolicy.Extension(type))
            && Guid.TryParseExact(name[..32], "N", out requestId) && requestId != Guid.Empty;
    }

    public async Task<Result<AssistantAudioInput>> StoreAsync(Guid requestId, byte[] content, string mediaType, DateTime expiresAtUtc, CancellationToken ct)
    {
        if (!files.IsAvailable) return FileErrors.Unavailable;
        var key = Key(requestId, mediaType);
        var sha256 = Convert.ToHexStringLower(SHA256.HashData(content));
        await using (var stream = new MemoryStream(content, false)) await files.WriteOnceAsync(key, stream, mediaType, ct);
        await using var stored = await files.OpenReadAsync(key, ct);
        if (stored is null) return FileErrors.Unavailable;
        using var copy = new MemoryStream();
        await stored.CopyToAsync(copy, ct);
        if (copy.Length != content.Length || Convert.ToHexStringLower(SHA256.HashData(copy.ToArray())) != sha256) return AiAssistantErrors.RequestReused;
        return new AssistantAudioInput(key, $"audio_{requestId:N}{AiAssistantAudioPolicy.Extension(mediaType)}", mediaType, content.Length, sha256, expiresAtUtc);
    }

    // Only this person's recording, while the normalization keeps its content; the descriptor names the exact private copy.
    public async Task<Result<AiAssistantAudioFileResponse>> OpenAsync(Guid owner, Guid requestId, CancellationToken ct)
    {
        if (!files.IsAvailable) return FileErrors.Unavailable;
        var receipt = await repository.GetReceiptAsync(owner, requestId, ct);
        if (receipt is null) return AiAssistantErrors.NotFound;
        var conversation = await repository.GetConversationAsync(owner, receipt.ConversationId, ct);
        var turn = await repository.GetTurnAsync(receipt.TurnId, ct);
        if (conversation?.ChannelId != AiAssistantChannelPolicy.HomeAudioChannelId || turn is null || turn.OwnerUserId != owner) return AiAssistantErrors.NotFound;
        if (turn.ContentExpiresAtUtc <= clock.GetUtcNow().UtcDateTime || turn.ProtectedQuestion is null) return AiAssistantErrors.Expired;
        var json = protector.Unprotect(owner, turn.ConversationId, turn.Id, "question", turn.PayloadRevision, turn.ProtectedQuestion);
        AssistantAudioInput? audio;
        try { audio = json is null ? null : JsonSerializer.Deserialize<AssistantAudioInput>(json, AiAssistantResponsePolicy.JsonOptions); }
        catch (JsonException) { audio = null; }
        if (audio is null || AiAssistantAudioPolicy.Validate(audio) is not null || audio.StorageKey != Key(requestId, AiAssistantAudioPolicy.MediaType(audio)))
            return AiAssistantErrors.ContentUnavailable;
        var stream = await files.OpenReadAsync(audio.StorageKey, ct);
        return stream is null ? AiAssistantErrors.ContentUnavailable : new AiAssistantAudioFileResponse(stream, AiAssistantAudioPolicy.MediaType(audio));
    }
}
