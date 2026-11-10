using System.Security.Cryptography;
using ArquitecturaBase.Application.Common.Validation;
using ArquitecturaBase.Application.Interfaces.Channels;
using ArquitecturaBase.Application.Interfaces.Integrations.Files;
using ArquitecturaBase.Application.Models.Assistant;
using ArquitecturaBase.Application.Models.StructuredData;
using ArquitecturaBase.Application.Modules.ArtificialIntelligence.Interfaces.Integrations;
using ArquitecturaBase.Application.Modules.ArtificialIntelligence.Interfaces.Persistence;
using ArquitecturaBase.Application.Modules.ArtificialIntelligence.Models;
using ArquitecturaBase.Domain.Files;
using ArquitecturaBase.Domain.Modules.ArtificialIntelligence;
using ArquitecturaBase.Domain.Results;

namespace ArquitecturaBase.Application.Modules.ArtificialIntelligence.Services;

// Stage 2: the private copy of a Home spreadsheet lives under a key the server derives from its request, so its retention keeps it
// while a turn that has it keeps its content, and the turn that brought it is told apart from those that inherit it. Its owner says
// who may attach and reads the file through the neutral port. A second host may win WriteOnce: the copy is re-read, never assumed ours.
internal sealed class AiAssistantHomeAttachmentPolicy(IPrivateFileStore files, IStructuredAttachmentReader reader, IAiAssistantReader turns,
    IAiConversationProtector protector)
{
    private const string Prefix = "ai/assistant/attachments/";
    // As long as the name of the prepared sheet, so the bubble, the title and the model read the same name.
    private const int NameLength = 120;

    public static string Key(Guid requestId, StructuredSpreadsheetKind kind) =>
        $"{Prefix}{requestId:N}{(kind == StructuredSpreadsheetKind.Csv ? ".csv" : ".xlsx")}";

    public static bool TryParseKey(string? key, out Guid requestId)
    {
        requestId = Guid.Empty;
        if (key is null || !key.StartsWith(Prefix, StringComparison.Ordinal)) return false;
        var name = key[Prefix.Length..];
        return name.Length > 32 && name[32..] is ".xlsx" or ".csv" && Guid.TryParseExact(name[..32], "N", out requestId) && requestId != Guid.Empty;
    }

    // The turn that brought a spreadsheet holds the copy of its own request; the turns that inherit it, another request's.
    public static bool IsOwn(string? attachmentKey, Guid requestId) => TryParseKey(attachmentKey, out var owner) && owner == requestId;

    // The person's file name without folders or control characters.
    public static string Name(string fileName)
    {
        ArgumentNullException.ThrowIfNull(fileName);
        var name = string.Concat(Path.GetFileName(fileName.Replace('\\', '/')).Where(c => !char.IsControl(c))).Trim();
        if (name.Length <= NameLength) return name;
        var kept = NameLength - 1;
        if (char.IsHighSurrogate(name[kept - 1])) kept--;
        return name[..kept] + "…";
    }

    // The descriptor of the copy, with the bytes' size and SHA-256 and the expiry of the turn's content, as a recording's.
    public static AssistantAttachmentInput Describe(Guid requestId, byte[] content, string name, StructuredSpreadsheet spreadsheet, DateTime expiresAtUtc) =>
        new(Key(requestId, spreadsheet.Kind), name, spreadsheet.MediaType, content.LongLength, Sha256(content), expiresAtUtc, spreadsheet);

    public static AiAssistantTurnAttachmentResponse Response(AssistantAttachmentInput attachment) => new(attachment.Name,
        attachment.Spreadsheet.Kind == StructuredSpreadsheetKind.Csv ? "csv" : "xlsx", attachment.Spreadsheet.RowCount, attachment.Size);

    // Who may attach in Home, for the turn and for the bootstrap: the owner allows the person and there is private storage for the copy.
    public async Task<bool> CanAttachAsync(Guid userId, CancellationToken ct) => files.IsAvailable && await reader.CanAttachAsync(userId, ct);

    // A file that is not read answers under the file, with a code for each reason, and never reaches the model.
    public async Task<Result<StructuredSpreadsheet>> PrepareAsync(byte[] content, string name, CancellationToken ct)
    {
        var prepared = await reader.PrepareAsync(content, name, ct);
        if (prepared.Spreadsheet is { } spreadsheet) return spreadsheet;
        return FieldErrors.On(prepared.Rejection switch
        {
            StructuredAttachmentRejection.Legacy => AiAssistantErrors.AttachmentLegacy,
            StructuredAttachmentRejection.Protected => AiAssistantErrors.AttachmentProtected,
            StructuredAttachmentRejection.MacroEnabled => AiAssistantErrors.AttachmentMacroEnabled,
            StructuredAttachmentRejection.TooLarge => AiAssistantErrors.AttachmentTooLarge,
            StructuredAttachmentRejection.Unreadable => AiAssistantErrors.AttachmentUnreadable,
            StructuredAttachmentRejection.Empty => AiAssistantErrors.AttachmentEmpty,
            _ => AiAssistantErrors.AttachmentUnavailable,
        }, "file");
    }

    // A retry is the same request again: this person's acceptance with that id brought its own spreadsheet, and the upload is that file
    // (SHA-256, size and name). The stored descriptor is the one to accept with, never a rebuilt one: its expiry is in the fingerprint.
    // Without an acceptance there is nothing to recover; without its own spreadsheet to compare with, the id was used for another request.
    public async Task<Result<AssistantAttachmentInput?>> FindRetryAsync(Guid owner, Guid requestId, byte[] content, string name, CancellationToken ct)
    {
        var turn = await turns.FindTurnByRequestAsync(owner, requestId, ct);
        if (turn is null) return Result.Success<AssistantAttachmentInput?>(null);
        var stored = turn.ChannelId == "home" && IsOwn(turn.AttachmentKey, turn.RequestId) && turn.ProtectedAttachment is { } protectedAttachment
            ? AiAssistantAttachmentPolicy.Read(protector.Unprotect(owner, turn.ConversationId, turn.Id, AiAssistantAttachmentPolicy.Purpose,
                turn.PayloadRevision, protectedAttachment))
            : null;
        if (stored is null || stored.Size != content.LongLength || stored.Name != name || stored.Sha256 != Sha256(content))
            return AiAssistantErrors.RequestReused;
        return stored;
    }

    public async Task<Result> StoreAsync(AssistantAttachmentInput attachment, byte[] content, CancellationToken ct)
    {
        if (!files.IsAvailable) return FileErrors.Unavailable;
        await using (var stream = new MemoryStream(content, false)) await files.WriteOnceAsync(attachment.StorageKey, stream, attachment.ContentType, ct);
        await using var stored = await files.OpenReadAsync(attachment.StorageKey, ct);
        if (stored is null) return FileErrors.Unavailable;
        using var copy = new MemoryStream();
        await stored.CopyToAsync(copy, ct);
        return copy.Length == attachment.Size && Sha256(copy.GetBuffer().AsSpan(0, (int)copy.Length)) == attachment.Sha256
            ? Result.Success() : AiAssistantErrors.RequestReused;
    }

    private static string Sha256(ReadOnlySpan<byte> content) => Convert.ToHexStringLower(SHA256.HashData(content));
}
