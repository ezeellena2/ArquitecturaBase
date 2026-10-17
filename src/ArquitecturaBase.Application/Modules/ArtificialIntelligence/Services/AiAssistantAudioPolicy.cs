using ArquitecturaBase.Application.Models.Assistant;
using ArquitecturaBase.Domain.Common;
using ArquitecturaBase.Domain.Modules.ArtificialIntelligence;
using ArquitecturaBase.Domain.Results;

namespace ArquitecturaBase.Application.Modules.ArtificialIntelligence.Services;

internal static class AiAssistantAudioPolicy
{
    public static string MediaType(AssistantAudioInput audio) => audio.ContentType.Split(';')[0].Trim().ToLowerInvariant();
    public static Error? Validate(AssistantAudioInput audio)
    {
        var type = MediaType(audio);
        return string.IsNullOrWhiteSpace(audio.StorageKey) || audio.StorageKey.Length > 500 || audio.StorageKey.Any(char.IsControl) ||
            audio.Name.Length is < 1 or > 64 || audio.Name.Any(c => !(char.IsAsciiLetterOrDigit(c) || c is '-' or '_' or '.')) ||
            audio.ContentType.Length > 128 || audio.ContentType.Any(char.IsControl) ||
            type is not ("audio/ogg" or "audio/wav") || !audio.Name.EndsWith(type == "audio/ogg" ? ".ogg" : ".wav", StringComparison.OrdinalIgnoreCase) ||
            audio.Size is <= 0 or > 25000000 || audio.Sha256.Length != 64 || !audio.Sha256.All(Uri.IsHexDigit) ||
            audio.ExpiresAtUtc.Kind != DateTimeKind.Utc ? AiAssistantErrors.Invalid : null;
    }
    public static bool Valid(AssistantNormalizedAudio audio, int maxCharacters) =>
        !string.IsNullOrWhiteSpace(audio.Text) && audio.Text.Length <= maxCharacters && StorableText.IsStorable(audio.Text) &&
        audio.Languages is { Count: <= 8 } && audio.Languages.All(code => code is { Length: >= 2 and <= 3 } && code.All(char.IsAsciiLetter)) &&
        audio.Languages.Distinct(StringComparer.OrdinalIgnoreCase).Count() == audio.Languages.Count;
}
