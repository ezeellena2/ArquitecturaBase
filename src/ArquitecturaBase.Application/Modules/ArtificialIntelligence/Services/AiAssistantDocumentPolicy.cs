using System.Text.Json;
using System.Text.Json.Serialization;
using ArquitecturaBase.Application.Models.Assistant;
using ArquitecturaBase.Application.Models.StructuredData;
using ArquitecturaBase.Application.Modules.ArtificialIntelligence.Configuration;
using ArquitecturaBase.Domain.Common;
using ArquitecturaBase.Domain.Modules.ArtificialIntelligence;
using ArquitecturaBase.Domain.Results;

namespace ArquitecturaBase.Application.Modules.ArtificialIntelligence.Services;

// ReadDocument: a server-owned descriptor of the private copy, like an audio's, read by a vision model with the sealed instructions
// document-read-v1@1. The reading is valid whole or it is not a reading: the engine never repairs, completes or cuts it.
internal static class AiAssistantDocumentPolicy
{
    public const string InstructionsId = "document-read-v1";
    public const int InstructionsVersion = 1;
    public static IReadOnlyList<string> MediaTypes { get; } = ["application/pdf", "image/jpeg", "image/png"];

    private const string StoredKind = "readDocument";
    private static readonly JsonSerializerOptions Stored = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase, allowIntegerValues: false) },
    };

    // A bounded private key and the person's file name, one of the three types as the server detected it, the attachment's size, 1 to 10
    // pages (a photo is one) and a lowercase SHA-256.
    public static Error? Validate(AssistantDocumentInput document) =>
        document.StorageKey is not { Length: > 0 and <= AiAssistantTurn.MaximumAttachmentKeyLength } key || string.IsNullOrWhiteSpace(key) || key.Any(char.IsControl) ||
        document.Name is not { Length: >= 1 and <= 255 } name || name.Any(char.IsControl) || name.IndexOfAny(['/', '\\']) >= 0 || name is "." or ".." ||
        document.ContentType is not { } type || !MediaTypes.Contains(type, StringComparer.Ordinal) ||
        document.Size is <= 0 or > StructuredAttachmentLimits.MaximumBytes ||
        document.Pages is < 1 or > StructuredAttachmentLimits.MaximumPages ||
        document.Sha256 is not { Length: 64 } sha || !sha.All(c => char.IsAsciiDigit(c) || c is >= 'a' and <= 'f') ||
        document.ExpiresAtUtc.Kind != DateTimeKind.Utc
            ? AiAssistantErrors.Invalid : null;

    // Whether a qualified profile reads this document: its type, its bytes and its pages.
    public static bool Fits(AssistantDocumentInput document, AiAssistantDocumentModelProfile profile) =>
        profile.ContentTypes.Contains(document.ContentType, StringComparer.Ordinal) && document.Size <= profile.MaximumDocumentBytes &&
        document.Pages <= profile.MaximumPages;

    // The descriptor is the turn's protected question, like an audio's.
    public static string Serialize(AssistantDocumentInput document) => JsonSerializer.Serialize(document, AiAssistantResponsePolicy.JsonOptions);

    public static AssistantDocumentInput? ReadDescriptor(string? json)
    {
        if (json is null) return null;
        try
        {
            var document = JsonSerializer.Deserialize<AssistantDocumentInput>(json, AiAssistantResponsePolicy.JsonOptions);
            return document is not null && Validate(document) is null ? document : null;
        }
        catch (JsonException) { return null; }
    }

    // Before validating, a blank language is none and a locale or an uppercase code ("es-AR", "EN") its lowercase primary subtag: the
    // model may answer with either instead of the code. Anything else stays as it came and the validation decides.
    public static StructuredDocumentReading Normalize(StructuredDocumentReading reading) => reading with { Language = PrimaryLanguage(reading.Language) };

    // Readability and problem within their sets, a language of two or three lowercase letters or none, a storable text within the bound,
    // and empty only when nothing could be read.
    public static bool Valid(StructuredDocumentReading reading, int maximumCharacters) =>
        Enum.IsDefined(reading.Readability) && (reading.Problem is not { } problem || Enum.IsDefined(problem)) &&
        (reading.Language is null || reading.Language is { Length: 2 or 3 } language && language.All(char.IsAsciiLetterLower)) &&
        reading.Text is { } text && text.Length <= maximumCharacters && StorableText.IsStorable(text) &&
        (reading.Readability == StructuredDocumentReadability.Unreadable || !string.IsNullOrWhiteSpace(text));

    // The closed answer of a completed reading, protected as the turn's answer.
    public static string SerializeReading(StructuredDocumentReading reading) => JsonSerializer.Serialize(new StoredReading(StoredKind,
        reading.Readability, reading.Problem, reading.Language, reading.Text, reading.Truncated), Stored);

    // A stored reading is bound by the ceiling, not by the current limit: lowering the limit never hides a reading already closed.
    public static StructuredDocumentReading? ReadStored(string json)
    {
        try
        {
            using var document = JsonDocument.Parse(json);
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object || !root.EnumerateObject().Select(property => property.Name).Order(StringComparer.Ordinal)
                    .SequenceEqual(["kind", "language", "problem", "readability", "text", "truncated"])) return null;
            var stored = root.Deserialize<StoredReading>(Stored);
            if (stored is not { Kind: StoredKind, Text: not null }) return null;
            var reading = new StructuredDocumentReading(stored.Readability, stored.Problem, stored.Language, stored.Text, stored.Truncated);
            return Valid(reading, AiAssistantLimits.ReadingCharactersCeiling) ? reading : null;
        }
        catch (Exception exception) when (exception is JsonException or InvalidOperationException) { return null; }
    }

    private static string? PrimaryLanguage(string? language)
    {
        if (string.IsNullOrWhiteSpace(language)) return null;
        var code = language.Trim();
        var cut = code.IndexOfAny(['-', '_']);
        return (cut < 0 ? code : code[..cut]).ToLowerInvariant();
    }

    private sealed record StoredReading(string Kind, StructuredDocumentReadability Readability, StructuredDocumentProblem? Problem,
        string? Language, string Text, bool Truncated);
}
