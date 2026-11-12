using System.Security.Cryptography;
using ArquitecturaBase.Application.Common.Validation;
using ArquitecturaBase.Application.Interfaces.Channels;
using ArquitecturaBase.Application.Interfaces.Integrations.Files;
using ArquitecturaBase.Application.Models.Assistant;
using ArquitecturaBase.Application.Models.StructuredData;
using ArquitecturaBase.Application.Modules.ArtificialIntelligence.Interfaces.Integrations;
using ArquitecturaBase.Application.Modules.ArtificialIntelligence.Interfaces.Persistence;
using ArquitecturaBase.Application.Modules.ArtificialIntelligence.Models;
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

    // Who may attach in Home, for the bootstrap: there is private storage for the copy and the owner allows the person a spreadsheet.
    public async Task<bool> CanAttachAsync(Guid userId, CancellationToken ct) => files.IsAvailable && (await reader.CanAttachAsync(userId, ct)).Spreadsheets;

    // The same rule for a turn, telling the installation apart from the account: without private storage spreadsheets are not available
    // here, and only the owner's refusal is the person's 403.
    public async Task<Error?> CheckAttachAsync(Guid userId, CancellationToken ct) =>
        !files.IsAvailable ? FieldErrors.On(AiAssistantErrors.AttachmentUnavailable, "file")
            : !(await reader.CanAttachAsync(userId, ct)).Spreadsheets ? AiAssistantErrors.AttachmentForbidden : null;

    // A file that is not read answers under the file, with a code for each reason, and never reaches the model. Home reads only
    // spreadsheets until it reads documents: a document, prepared or rejected, is a file it cannot read, as before, because the texts
    // speak of spreadsheets.
    public async Task<Result<StructuredSpreadsheet>> PrepareAsync(byte[] content, string name, CancellationToken ct)
    {
        var prepared = await reader.PrepareAsync(content, name, ct);
        if (prepared.Spreadsheet is { } spreadsheet) return spreadsheet;
        var rejection = prepared.Document is not null || prepared.FileKind is StructuredAttachmentFileKind.Pdf or StructuredAttachmentFileKind.Image
            or StructuredAttachmentFileKind.Word ? StructuredAttachmentRejection.Unreadable : prepared.Rejection;
        return FieldErrors.On(rejection switch
        {
            StructuredAttachmentRejection.Legacy => AiAssistantErrors.AttachmentLegacy,
            StructuredAttachmentRejection.Protected => AiAssistantErrors.AttachmentProtected,
            StructuredAttachmentRejection.MacroEnabled => AiAssistantErrors.AttachmentMacroEnabled,
            StructuredAttachmentRejection.TooLarge or StructuredAttachmentRejection.TooManyPages => AiAssistantErrors.AttachmentTooLarge,
            StructuredAttachmentRejection.Unreadable => AiAssistantErrors.AttachmentUnreadable,
            StructuredAttachmentRejection.Empty => AiAssistantErrors.AttachmentEmpty,
            _ => AiAssistantErrors.AttachmentUnavailable,
        }, "file");
    }

    // A retry is the same request again: this person's acceptance with that id brought its own spreadsheet, and the upload is that file.
    // What was sent (SHA-256 and size, in the clear) is compared first, and only then the stored descriptor is read, with its name, to
    // accept with it without reading or storing again. A turn whose copy is gone (purged or retired) answers as the JSON turn would, not
    // found, and one that cannot be read, unavailable. Without an acceptance there is nothing to recover.
    public async Task<Result<AssistantAttachmentInput?>> FindRetryAsync(Guid owner, Guid requestId, byte[] content, string name, CancellationToken ct)
    {
        var turn = await turns.FindTurnByRequestAsync(owner, requestId, ct);
        if (turn is null) return Result.Success<AssistantAttachmentInput?>(null);
        if (turn.ChannelId != "home" || !IsOwn(turn.AttachmentKey, turn.RequestId) || turn.AttachmentSize != content.LongLength
            || turn.AttachmentSha256 != Sha256(content)) return AiAssistantErrors.RequestReused;
        var protectedAttachment = (await turns.ListProtectedAttachmentsAsync(owner, [turn.Id], ct)).GetValueOrDefault(turn.Id);
        if (protectedAttachment is null) return AiAssistantErrors.NotFound;
        var stored = AiAssistantAttachmentPolicy.Read(protector.Unprotect(owner, turn.ConversationId, turn.Id, AiAssistantAttachmentPolicy.Purpose,
            turn.PayloadRevision, protectedAttachment));
        if (stored is null) return AiAssistantErrors.ContentUnavailable;
        return stored.Name == name ? stored : AiAssistantErrors.RequestReused;
    }

    // A copy that cannot be read back says so under the file, in neutral words; another content under the same request is a conflict.
    public async Task<Result> StoreAsync(AssistantAttachmentInput attachment, byte[] content, CancellationToken ct)
    {
        if (!files.IsAvailable) return FieldErrors.On(AiAssistantErrors.AttachmentNotStored, "file");
        await using (var stream = new MemoryStream(content, false)) await files.WriteOnceAsync(attachment.StorageKey, stream, attachment.ContentType, ct);
        await using var stored = await files.OpenReadAsync(attachment.StorageKey, ct);
        if (stored is null) return FieldErrors.On(AiAssistantErrors.AttachmentNotStored, "file");
        using var copy = new MemoryStream();
        await stored.CopyToAsync(copy, ct);
        return copy.Length == attachment.Size && Sha256(copy.GetBuffer().AsSpan(0, (int)copy.Length)) == attachment.Sha256
            ? Result.Success() : AiAssistantErrors.RequestReused;
    }

    private static string Sha256(ReadOnlySpan<byte> content) => Convert.ToHexStringLower(SHA256.HashData(content));
}
