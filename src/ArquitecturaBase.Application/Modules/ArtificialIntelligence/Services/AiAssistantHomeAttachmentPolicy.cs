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

// Stage 2 and 3: the private copy of a file a Home turn brings (a spreadsheet or a Word) lives under a key the server derives from its
// request, so its retention keeps it while a turn that has it keeps its content, and the turn that brought it is told apart from those
// that inherit it. Its owner says who may attach each kind and reads the file through the neutral port. A second host may win WriteOnce:
// the copy is re-read, never assumed ours.
internal sealed class AiAssistantHomeAttachmentPolicy(IPrivateFileStore files, IStructuredAttachmentReader reader, IAiAssistantReader turns,
    IAiConversationProtector protector)
{
    private const string Prefix = "ai/assistant/attachments/";
    // As long as the name of the prepared sheet, so the bubble, the title and the model read the same name.
    private const int NameLength = 120;

    public static string Key(Guid requestId, StructuredSpreadsheetKind kind) =>
        $"{Prefix}{requestId:N}{(kind == StructuredSpreadsheetKind.Csv ? ".csv" : ".xlsx")}";

    public static string WordKey(Guid requestId) => $"{Prefix}{requestId:N}.docx";

    // A Home copy names its request: a file a turn brought (a spreadsheet or a Word) or a document read before its turn (a PDF or a photo).
    public static bool TryParseKey(string? key, out Guid requestId) =>
        TryParseFileKey(key, out requestId) || AiAssistantHomeDocumentPolicy.TryParseKey(key, out requestId);

    // Only the copy of a file a turn brought, which names that turn's request.
    public static bool TryParseFileKey(string? key, out Guid requestId)
    {
        requestId = Guid.Empty;
        if (key is null || !key.StartsWith(Prefix, StringComparison.Ordinal)) return false;
        var name = key[Prefix.Length..];
        return name.Length > 32 && name[32..] is ".xlsx" or ".csv" or ".docx" && Guid.TryParseExact(name[..32], "N", out requestId) && requestId != Guid.Empty;
    }

    // The turn that brought a file holds the copy of its own request; the turns that inherit it, another request's.
    public static bool IsOwn(string? attachmentKey, Guid requestId) => TryParseFileKey(attachmentKey, out var owner) && owner == requestId;

    // The same for a row, which also tells a document apart: the turn that brought it named its reading, sealed in its profile.
    public static bool IsOwn(AiAssistantTurnRow row) => TryParseFileKey(row.AttachmentKey, out var owner) ? owner == row.RequestId
        : AiAssistantHomeDocumentPolicy.TryParseKey(row.AttachmentKey, out owner) && AiAssistantChannelPolicy.Read(row.ExecutionProfileJson) is
            { IsSuccess: true } profile && profile.Value.DocumentRequestId == owner;

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

    // The same for what file-turns prepared: a spreadsheet, or a Word with its reading fitted to the documents' budget (null when it cannot
    // be attached).
    public static AssistantAttachmentInput? Describe(Guid requestId, byte[] content, string name, StructuredAttachmentPreparation prepared, DateTime expiresAtUtc) =>
        prepared.Spreadsheet is { } spreadsheet ? Describe(requestId, content, name, spreadsheet, expiresAtUtc)
            : prepared.Document is { } document ? AiAssistantAttachmentPolicy.ForDocument(WordKey(requestId), name, expiresAtUtc, document) : null;

    // The file a Home turn brought, for its bubble: a spreadsheet with its rows, or a document with its pages (a photo is one; a Word has none).
    public static AiAssistantTurnAttachmentResponse? Response(AssistantAttachmentInput attachment) => attachment switch
    {
        { Spreadsheet: { } spreadsheet } => new(attachment.Name, spreadsheet.Kind == StructuredSpreadsheetKind.Csv ? "csv" : "xlsx", spreadsheet.RowCount,
            attachment.Size),
        { Document: { } document } => new(attachment.Name, document.Kind.ToString().ToLowerInvariant(), null, attachment.Size)
        {
            Pages = document.Kind switch { StructuredDocumentKind.Pdf => document.Pages, StructuredDocumentKind.Docx => null, _ => 1 },
        },
        _ => null,
    };

    // What Home lets this person attach, for the bootstrap: the owner's answer, only when there is private storage for the copy.
    public async Task<StructuredAttachmentAccess> CanAttachAsync(Guid userId, CancellationToken ct) =>
        files.IsAvailable ? await reader.CanAttachAsync(userId, ct) : StructuredAttachmentAccess.None;

    // The same rule before a file is read, telling the installation apart from the account: without private storage nothing is attached
    // here, and a person the owner allows neither kind gets the 403. Each kind is checked again once the bytes say which it is.
    public async Task<Result<StructuredAttachmentAccess>> CheckAttachAsync(Guid userId, CancellationToken ct)
    {
        if (!files.IsAvailable) return FieldErrors.On(AiAssistantErrors.AttachmentUnavailable, "file");
        var access = await reader.CanAttachAsync(userId, ct);
        return access.Spreadsheets || access.Documents ? access : AiAssistantErrors.AttachmentForbidden;
    }

    // A file that is not read answers under the file, with a code for each reason, and never reaches the model. File-turns takes a
    // spreadsheet or a Word, each for whom the owner allows it; a photo or a PDF is read before its turn, so here it is a request error.
    public async Task<Result<StructuredAttachmentPreparation>> PrepareAsync(byte[] content, string name, StructuredAttachmentAccess access, CancellationToken ct)
    {
        var prepared = await reader.PrepareAsync(content, name, ct);
        if (prepared.Document is { Kind: not StructuredDocumentKind.Docx } || prepared.FileKind is StructuredAttachmentFileKind.Pdf
            or StructuredAttachmentFileKind.Image) return FieldErrors.On(AiAssistantErrors.Invalid, "file");
        if ((prepared.Spreadsheet is not null || prepared.FileKind == StructuredAttachmentFileKind.Spreadsheet) && !access.Spreadsheets)
            return AiAssistantErrors.AttachmentForbidden;
        if ((prepared.Document is not null || prepared.FileKind == StructuredAttachmentFileKind.Word) && !access.Documents)
            return AiAssistantErrors.AttachmentDocumentForbidden;
        return prepared.Spreadsheet is not null || prepared.Document is not null ? prepared : Rejected(prepared);
    }

    // Why a file was not read, under the file: a document's text names what it was; a spreadsheet's, or one the owner could not tell,
    // keeps the texts of spreadsheets.
    public static Error Rejected(StructuredAttachmentPreparation prepared) => FieldErrors.On((prepared.FileKind, prepared.Rejection) switch
    {
        (_, StructuredAttachmentRejection.TooManyPages) => AiAssistantErrors.AttachmentTooManyPages,
        (StructuredAttachmentFileKind.Pdf, StructuredAttachmentRejection.Protected) => AiAssistantErrors.AttachmentProtectedPdf,
        (StructuredAttachmentFileKind.Pdf, _) => AiAssistantErrors.AttachmentUnreadablePdf,
        (StructuredAttachmentFileKind.Image, StructuredAttachmentRejection.TooLarge) => AiAssistantErrors.AttachmentTooLargeImage,
        (StructuredAttachmentFileKind.Image, _) => AiAssistantErrors.AttachmentUnreadableImage,
        (StructuredAttachmentFileKind.Word, StructuredAttachmentRejection.Legacy) => AiAssistantErrors.AttachmentLegacyWord,
        (StructuredAttachmentFileKind.Word, StructuredAttachmentRejection.Protected) => AiAssistantErrors.AttachmentProtectedWord,
        (StructuredAttachmentFileKind.Word, StructuredAttachmentRejection.MacroEnabled) => AiAssistantErrors.AttachmentMacroEnabledWord,
        (StructuredAttachmentFileKind.Word, StructuredAttachmentRejection.Empty) => AiAssistantErrors.AttachmentEmptyWord,
        (StructuredAttachmentFileKind.Word, _) => AiAssistantErrors.AttachmentUnreadableWord,
        (_, StructuredAttachmentRejection.Legacy) => AiAssistantErrors.AttachmentLegacy,
        (_, StructuredAttachmentRejection.Protected) => AiAssistantErrors.AttachmentProtected,
        (_, StructuredAttachmentRejection.MacroEnabled) => AiAssistantErrors.AttachmentMacroEnabled,
        (_, StructuredAttachmentRejection.TooLarge) => AiAssistantErrors.AttachmentTooLarge,
        (_, StructuredAttachmentRejection.Unreadable) => AiAssistantErrors.AttachmentUnreadable,
        (_, StructuredAttachmentRejection.Empty) => AiAssistantErrors.AttachmentEmpty,
        _ => AiAssistantErrors.AttachmentUnavailable,
    }, "file");

    // A retry is the same request again: this person's acceptance with that id brought its own file, and the upload is that file.
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
        var notStored = FieldErrors.On(attachment.Document is null ? AiAssistantErrors.AttachmentNotStored : AiAssistantErrors.AttachmentDocumentNotStored, "file");
        if (!files.IsAvailable) return notStored;
        await using (var stream = new MemoryStream(content, false)) await files.WriteOnceAsync(attachment.StorageKey, stream, attachment.ContentType, ct);
        await using var stored = await files.OpenReadAsync(attachment.StorageKey, ct);
        if (stored is null) return notStored;
        using var copy = new MemoryStream();
        await stored.CopyToAsync(copy, ct);
        return copy.Length == attachment.Size && Sha256(copy.GetBuffer().AsSpan(0, (int)copy.Length)) == attachment.Sha256
            ? Result.Success() : AiAssistantErrors.RequestReused;
    }

    private static string Sha256(ReadOnlySpan<byte> content) => Convert.ToHexStringLower(SHA256.HashData(content));
}
