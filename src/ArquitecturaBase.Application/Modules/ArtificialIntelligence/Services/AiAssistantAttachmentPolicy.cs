using System.Globalization;
using System.Text.Json;
using ArquitecturaBase.Application.Models.Assistant;
using ArquitecturaBase.Application.Models.StructuredData;
using ArquitecturaBase.Application.Modules.ArtificialIntelligence.Interfaces.Persistence;
using ArquitecturaBase.Application.Modules.ArtificialIntelligence.Models;
using ArquitecturaBase.Application.Modules.ArtificialIntelligence.Resources;
using ArquitecturaBase.Domain.Modules.ArtificialIntelligence;
using ArquitecturaBase.Domain.Results;

namespace ArquitecturaBase.Application.Modules.ArtificialIntelligence.Services;

// Stage 2: a spreadsheet is a server-owned descriptor, like an audio's, plus the sheet its owner prepared. The engine never opens
// the copy; the prepared sheet is opaque here and only its own type serializes it.
internal static class AiAssistantAttachmentPolicy
{
    public const string Purpose = "attachment";

    // A bounded private key and a safe name, an import's size, a lowercase SHA-256 and the prepared sheet within its cap, prepared from
    // the same file the descriptor names.
    public static Error? Validate(AssistantAttachmentInput attachment) =>
        attachment.Spreadsheet is null || attachment.Spreadsheet.Sha256 != attachment.Sha256 || attachment.Spreadsheet.Size != attachment.Size ||
        attachment.StorageKey is not { Length: > 0 and <= AiAssistantTurn.MaximumAttachmentKeyLength } key || string.IsNullOrWhiteSpace(key) || key.Any(char.IsControl) ||
        attachment.Name is not { Length: >= 1 and <= 255 } name || name.Any(char.IsControl) || name.IndexOfAny(['/', '\\']) >= 0 || name is "." or ".." ||
        attachment.ContentType is not { Length: >= 1 and <= 128 } type || string.IsNullOrWhiteSpace(type) || type.Any(char.IsControl) ||
        attachment.Size is <= 0 or > AssistantAttachmentInput.MaximumBytes ||
        attachment.Sha256 is not { Length: 64 } sha || !sha.All(c => char.IsAsciiDigit(c) || c is >= 'a' and <= 'f') ||
        attachment.ExpiresAtUtc.Kind != DateTimeKind.Utc ||
        JsonSerializer.SerializeToUtf8Bytes(attachment.Spreadsheet, AiAssistantResponsePolicy.JsonOptions).Length > StructuredSpreadsheet.MaximumBytes
            ? AiAssistantErrors.Invalid : null;

    // The rows never bring their sealed spreadsheets. Who shows or names one asks, in one query, only for the turns that brought theirs:
    // a Home copy names its request; any other copy was brought by the earliest turn that has it, when that turn is in the list.
    public static async Task<IReadOnlyList<AiAssistantTurnRow>> WithOwnAttachmentsAsync(IAiAssistantReader reader, Guid ownerUserId,
        IReadOnlyList<AiAssistantTurnRow> rows, CancellationToken ct)
    {
        var own = rows.Where(row => row.AttachmentKey is not null).GroupBy(row => (row.ConversationId, row.AttachmentKey))
            .Select(copy => copy.MinBy(row => row.Ordinal)!)
            .Where(row => !AiAssistantHomeAttachmentPolicy.TryParseKey(row.AttachmentKey, out _) || AiAssistantHomeAttachmentPolicy.IsOwn(row.AttachmentKey, row.RequestId))
            .Select(row => row.Id).ToArray();
        if (own.Length == 0) return rows;
        var sealedAttachments = await reader.ListProtectedAttachmentsAsync(ownerUserId, own, ct);
        return [.. rows.Select(row => sealedAttachments.TryGetValue(row.Id, out var protectedAttachment) ? row with { ProtectedAttachment = protectedAttachment } : row)];
    }

    public static string Serialize(AssistantAttachmentInput attachment) => JsonSerializer.Serialize(attachment, AiAssistantResponsePolicy.JsonOptions);

    // What a turn sealed: anything that is not a valid descriptor is not a spreadsheet.
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

    // What the model reads of a turn with a spreadsheet: what was written and said, and then the sheet as data, in compact JSON.
    public static string Message(string question, StructuredSpreadsheet spreadsheet, string culture)
    {
        var block = string.Format(CultureInfo.InvariantCulture, AssistantTexts.Get("Prompt.Attachment", culture),
            JsonSerializer.Serialize(spreadsheet, AiAssistantResponsePolicy.JsonOptions));
        return question.Trim().Length == 0 ? block : question + "\n\n" + block;
    }
}
