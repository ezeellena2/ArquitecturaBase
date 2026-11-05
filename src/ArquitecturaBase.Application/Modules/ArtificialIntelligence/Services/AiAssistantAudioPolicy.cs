using System.Text.Json;
using ArquitecturaBase.Application.Models.Assistant;
using ArquitecturaBase.Domain.Common;
using ArquitecturaBase.Domain.Modules.ArtificialIntelligence;
using ArquitecturaBase.Domain.Results;

namespace ArquitecturaBase.Application.Modules.ArtificialIntelligence.Services;

internal static class AiAssistantAudioPolicy
{
    // Each accepted media type and the extension its private name carries; a browser records WebM or MP4 (IA4).
    private static readonly Dictionary<string, string> Extensions = new(StringComparer.Ordinal)
        { ["audio/ogg"] = ".ogg", ["audio/wav"] = ".wav", ["audio/webm"] = ".webm", ["audio/mp4"] = ".m4a" };
    public static IReadOnlyCollection<string> MediaTypes => Extensions.Keys;
    // A transcript is as long as a question may be; the browser records at most a million bytes, like WhatsApp normalizes.
    public const int MaximumTranscriptCharacters = 4000;
    public static string Extension(string mediaType) => Extensions[mediaType];
    public static string MediaType(AssistantAudioInput audio) => audio.ContentType.Split(';')[0].Trim().ToLowerInvariant();
    public static Error? Validate(AssistantAudioInput audio)
    {
        var type = MediaType(audio);
        return string.IsNullOrWhiteSpace(audio.StorageKey) || audio.StorageKey.Length > 500 || audio.StorageKey.Any(char.IsControl) ||
            audio.Name.Length is < 1 or > 64 || audio.Name.Any(c => !(char.IsAsciiLetterOrDigit(c) || c is '-' or '_' or '.')) ||
            audio.ContentType.Length > 128 || audio.ContentType.Any(char.IsControl) ||
            !Extensions.TryGetValue(type, out var extension) || !audio.Name.EndsWith(extension, StringComparison.OrdinalIgnoreCase) ||
            audio.Size is <= 0 or > 25000000 || audio.Sha256.Length != 64 || !audio.Sha256.All(Uri.IsHexDigit) ||
            audio.ExpiresAtUtc.Kind != DateTimeKind.Utc ? AiAssistantErrors.Invalid : null;
    }
    private static ReadOnlySpan<byte> Ebml => [0x1A, 0x45, 0xDF, 0xA3];

    // What a browser recording is, by its bytes and never by what the browser declares: WebM opens with the EBML header and MP4
    // names its brand in an ftyp box.
    public static string? Detect(ReadOnlySpan<byte> head) =>
        head.Length >= 4 && head[..4].SequenceEqual(Ebml) ? "audio/webm"
            : head.Length >= 12 && head.Slice(4, 4).SequenceEqual("ftyp"u8) ? "audio/mp4" : null;

    // The closed result a completed normalization stored; anything else is not a transcript.
    public static AssistantNormalizedAudio? ReadNormalized(string json, int maxCharacters)
    {
        try
        {
            using var document = JsonDocument.Parse(json);
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object || root.EnumerateObject().Select(property => property.Name).Order(StringComparer.Ordinal)
                    .SequenceEqual(["kind", "languages", "text"]) is false || root.GetProperty("kind").GetString() != "normalizedAudio") return null;
            var audio = root.Deserialize<AssistantNormalizedAudio>(AiAssistantResponsePolicy.JsonOptions);
            return audio is not null && Valid(audio, maxCharacters) ? audio : null;
        }
        catch (Exception exception) when (exception is JsonException or InvalidOperationException) { return null; }
    }

    // What the model reads of a dictated turn: the typed text, if any, and then what was said.
    public static string Spoken(string question, string? transcript) =>
        transcript is null ? question : question.Trim().Length == 0 ? transcript : question + "\n\n" + transcript;

    public static bool Valid(AssistantNormalizedAudio audio, int maxCharacters) =>
        !string.IsNullOrWhiteSpace(audio.Text) && audio.Text.Length <= maxCharacters && StorableText.IsStorable(audio.Text) &&
        audio.Languages is { Count: <= 8 } && audio.Languages.All(code => code is { Length: >= 2 and <= 3 } && code.All(char.IsAsciiLetter)) &&
        audio.Languages.Distinct(StringComparer.OrdinalIgnoreCase).Count() == audio.Languages.Count;
}
