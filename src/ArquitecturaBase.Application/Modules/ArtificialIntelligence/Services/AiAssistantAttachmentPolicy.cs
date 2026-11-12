using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;
using ArquitecturaBase.Application.Models.Assistant;
using ArquitecturaBase.Application.Models.StructuredData;
using ArquitecturaBase.Application.Modules.ArtificialIntelligence.Configuration;
using ArquitecturaBase.Application.Modules.ArtificialIntelligence.Interfaces.Persistence;
using ArquitecturaBase.Application.Modules.ArtificialIntelligence.Models;
using ArquitecturaBase.Application.Modules.ArtificialIntelligence.Resources;
using ArquitecturaBase.Domain.Modules.ArtificialIntelligence;
using ArquitecturaBase.Domain.Results;

namespace ArquitecturaBase.Application.Modules.ArtificialIntelligence.Services;

// A file attached to an interpretation (a spreadsheet, Stage 2, or a read document) is a server-owned descriptor, like an audio's, plus
// what its owner prepared from the same bytes. The engine never opens the copy; what was prepared is opaque here and only its own types
// serialize it.
internal static class AiAssistantAttachmentPolicy
{
    public const string Purpose = "attachment";

    // What the model reads of a document: its kind, name and pages and its reading, the values in words and the missing ones left out.
    private static readonly JsonSerializerOptions DocumentOptions = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase, allowIntegerValues: false) },
    };

    // A bounded private key and a safe name, an import's size, a lowercase SHA-256 and exactly one file prepared from the same bytes the
    // descriptor names: the sheet within its cap, or a read document, and the ones after it, each within its share.
    public static Error? Validate(AssistantAttachmentInput attachment) =>
        attachment.StorageKey is not { Length: > 0 and <= AiAssistantTurn.MaximumAttachmentKeyLength } key || string.IsNullOrWhiteSpace(key) || key.Any(char.IsControl) ||
        !AssistantAttachmentInput.IsSafeName(attachment.Name) ||
        attachment.ContentType is not { Length: >= 1 and <= 128 } type || string.IsNullOrWhiteSpace(type) || type.Any(char.IsControl) ||
        attachment.Size is <= 0 or > AssistantAttachmentInput.MaximumBytes ||
        attachment.Sha256 is not { Length: 64 } sha || !sha.All(c => char.IsAsciiDigit(c) || c is >= 'a' and <= 'f') ||
        attachment.ExpiresAtUtc.Kind != DateTimeKind.Utc ||
        !(attachment.Spreadsheet is { } spreadsheet ? attachment.Document is null && attachment.MoreDocuments is null && SheetFits(attachment, spreadsheet)
            : attachment.Document is { } document && DocumentsFit(attachment, document))
            ? AiAssistantErrors.Invalid : null;

    private static bool SheetFits(AssistantAttachmentInput attachment, StructuredSpreadsheet spreadsheet) =>
        spreadsheet.Sha256 == attachment.Sha256 && spreadsheet.Size == attachment.Size &&
        JsonSerializer.SerializeToUtf8Bytes(spreadsheet, AiAssistantResponsePolicy.JsonOptions).Length <= StructuredSpreadsheet.MaximumBytes;

    // The first document is the copy's; the others go as data. Each one is admitted in its share by the rule and the measure the builder
    // uses, with a whole valid reading: the engine never cuts what it receives.
    private static bool DocumentsFit(AssistantAttachmentInput attachment, StructuredDocument document)
    {
        if (document.Sha256 != attachment.Sha256 || document.Size != attachment.Size
            || attachment.MoreDocuments is { Count: 0 or > AssistantAttachmentInput.MaximumMoreDocuments }) return false;
        List<StructuredDocument?> documents = [document, .. attachment.MoreDocuments ?? Array.Empty<StructuredDocument>()];
        return documents.Select((each, index) => AssistantAttachmentInput.Admits(each, AssistantAttachmentInput.DocumentBytes(index, documents.Count))
            && AiAssistantDocumentPolicy.Valid(each!.Reading!, AiAssistantLimits.ReadingCharactersCeiling)).All(fits => fits);
    }

    // A read document attached as its owner built it, or null when it cannot be attached (no reading, an unsafe name, no room for its text).
    public static AssistantAttachmentInput? ForDocument(string storageKey, string name, DateTime expiresAtUtc, StructuredDocument document)
    {
        if (!AssistantAttachmentInput.Admits(AssistantAttachmentInput.Fit(document, AssistantAttachmentInput.DocumentsMaximumBytes),
            AssistantAttachmentInput.DocumentsMaximumBytes)) return null;
        var attachment = AssistantAttachmentInput.ForDocument(storageKey, name, expiresAtUtc, document);
        return Validate(attachment) is null ? attachment : null;
    }

    // The rows never bring their sealed files. Who shows or names one asks, in one query, only for the turns that brought theirs:
    // a Home copy says which turns brought it; any other copy was brought by the earliest turn that has it, when that turn is in the list.
    public static async Task<IReadOnlyList<AiAssistantTurnRow>> WithOwnAttachmentsAsync(IAiAssistantReader reader, Guid ownerUserId,
        IReadOnlyList<AiAssistantTurnRow> rows, CancellationToken ct)
    {
        var copies = rows.Where(row => row.AttachmentKey is not null).ToLookup(row => AiAssistantHomeAttachmentPolicy.TryParseKey(row.AttachmentKey, out _));
        var own = copies[true].Where(AiAssistantHomeAttachmentPolicy.IsOwn)
            .Concat(copies[false].GroupBy(row => (row.ConversationId, row.AttachmentKey)).Select(copy => copy.MinBy(row => row.Ordinal)!))
            .Select(row => row.Id).ToArray();
        if (own.Length == 0) return rows;
        var sealedAttachments = await reader.ListProtectedAttachmentsAsync(ownerUserId, own, ct);
        return [.. rows.Select(row => sealedAttachments.TryGetValue(row.Id, out var protectedAttachment) ? row with { ProtectedAttachment = protectedAttachment } : row)];
    }

    public static string Serialize(AssistantAttachmentInput attachment) => JsonSerializer.Serialize(attachment, AiAssistantResponsePolicy.JsonOptions);

    // What a turn sealed: anything that is not a valid descriptor is not a file.
    public static AssistantAttachmentInput? Read(string? json)
    {
        if (json is null) return null;
        try
        {
            var attachment = JsonSerializer.Deserialize<AssistantAttachmentInput>(json, AiAssistantResponsePolicy.JsonOptions);
            return attachment is not null && Validate(attachment) is null ? attachment : null;
        }
        catch (JsonException) { return null; }
    }

    // What the model reads of a turn with a file: what was written and said, and then the file as data, in compact JSON: the sheet, or
    // the read document, the ones after it numbered with it.
    public static string Message(string question, AssistantAttachmentInput attachment, string culture)
    {
        var block = attachment.Document is { } document
            ? string.Format(CultureInfo.InvariantCulture, AssistantTexts.Get("Prompt.DocumentAttachment", culture), attachment.MoreDocuments is { } more
                ? string.Concat(more.Prepend(document).Select((each, index) => "\n" + string.Format(CultureInfo.InvariantCulture,
                    AssistantTexts.Get("Prompt.DocumentNumber", culture), index + 1, DocumentJson(each))))
                : DocumentJson(document))
            : string.Format(CultureInfo.InvariantCulture, AssistantTexts.Get("Prompt.Attachment", culture),
                JsonSerializer.Serialize(attachment.Spreadsheet, AiAssistantResponsePolicy.JsonOptions));
        return question.Trim().Length == 0 ? block : question + "\n\n" + block;
    }

    // The file a turn's catalog was decided for: the kind sealed with it or, for a turn with a file sealed before documents, a
    // spreadsheet. A kind sealed without a file is not a seal.
    public static AssistantAttachmentKind? Sealed(AiAssistantExecutionProfileSnapshot profile, string? attachmentKey) => (profile.Attachment, attachmentKey) switch
    {
        (AssistantAttachmentKind.None, null) => AssistantAttachmentKind.None,
        (AssistantAttachmentKind.None, _) => AssistantAttachmentKind.Spreadsheet,
        (_, null) => null,
        var (kind, _) => kind,
    };

    private static string DocumentJson(StructuredDocument document) => JsonSerializer.Serialize(new DocumentBlock(document.Kind, document.FileName,
        document.Pages, document.Reading!.Readability, document.Reading.Problem, document.Reading.Language, document.Reading.Text, document.Reading.Truncated),
        DocumentOptions);

    private sealed record DocumentBlock(StructuredDocumentKind Kind, string Name,
        [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] int? Pages, StructuredDocumentReadability Readability,
        [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] StructuredDocumentProblem? Problem,
        [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? Language, string Text, bool Truncated);
}
