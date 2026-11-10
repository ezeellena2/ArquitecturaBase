using System.Security.Cryptography;
using ArquitecturaBase.Application.Interfaces.Channels;
using ArquitecturaBase.Application.Interfaces.Integrations.Files;
using ArquitecturaBase.Application.Models.Assistant;
using ArquitecturaBase.Application.Models.StructuredData;
using ArquitecturaBase.Application.Modules.WhatsApp.Interfaces.Integrations;
using ArquitecturaBase.Application.Modules.WhatsApp.Interfaces.Persistence;
using ArquitecturaBase.Application.Modules.WhatsApp.Models;
using ArquitecturaBase.Domain.Files;
using ArquitecturaBase.Domain.Modules.WhatsApp;
using ArquitecturaBase.Domain.Results;

namespace ArquitecturaBase.Application.Modules.WhatsApp.Services;

// Stage 2 of the assistant authoring: a spreadsheet received by WhatsApp is downloaded only for the linked, active account of an
// automatic conversation, whose number is the chat's, that the owner of spreadsheets lets attach, and with private storage for the
// copy. Downloading, checking, preparing and copying happen outside the channel transaction: the size Meta declares and the bytes are
// bounded, the bytes must have Meta's SHA-256, the owner prepares them through the neutral port, and the copy is written once and read
// back, never assumed ours.
internal sealed class WhatsAppSpreadsheetPreparationPolicy(IWhatsAppMediaClient media, IStructuredAttachmentReader attachments,
    IPrivateFileStore files, IWhatsAppFormRecipientReader recipients, WhatsAppFormActionPolicy recipientGuard)
{
    private const string Prefix = "whatsapp/documents/";

    public static string Key(Guid messageId, StructuredSpreadsheetKind kind) =>
        $"{Prefix}{messageId:N}{(kind == StructuredSpreadsheetKind.Csv ? ".csv" : ".xlsx")}";

    public static bool TryParseKey(string? key, out Guid messageId)
    {
        messageId = Guid.Empty;
        if (key is null || !key.StartsWith(Prefix, StringComparison.Ordinal)) return false;
        var name = key[Prefix.Length..];
        return name.Length > 32 && name[32..] is ".xlsx" or ".csv" && Guid.TryParseExact(name[..32], "N", out messageId) && messageId != Guid.Empty;
    }

    public async Task<bool> CanDownloadAsync(WhatsAppSpreadsheetSourceRow source, CancellationToken ct)
    {
        if (!files.IsAvailable || source.Mode != WhatsAppConversationMode.Assistant || source.LinkedUserId is not { } subject) return false;
        var recipient = await recipients.FindAsync(source.ContactId, ct);
        return recipient is { IsActive: true } && recipient.UserId == subject && recipient.LinkRevision == source.LinkRevision
            && await recipientGuard.CanReachAsync(recipient.WaId, subject, ct) && await attachments.CanAttachAsync(subject, ct);
    }

    public async Task<Result<WhatsAppPreparedSpreadsheet>> AcquireAsync(WhatsAppSpreadsheetSourceRow source, CancellationToken ct)
    {
        if (source.MediaId is null || source.ContentType is null || source.Sha256 is null) return FileErrors.InvalidMetadata;
        var downloaded = await media.DownloadAsync(source.MediaId, source.ContentType, source.Sha256, AssistantAttachmentInput.MaximumBytes, ct);
        // Over the limit, by the size Meta declares or by the bytes, is the person's reason, as a sheet over the import's limits.
        if (downloaded.IsFailure)
            return downloaded.Error == FileErrors.TooLarge ? WhatsAppPreparedSpreadsheet.Rejected(StructuredAttachmentRejection.TooLarge) : downloaded.Error;
        var content = downloaded.Value.Content;
        if (content.LongLength > AssistantAttachmentInput.MaximumBytes) return WhatsAppPreparedSpreadsheet.Rejected(StructuredAttachmentRejection.TooLarge);
        var sha256 = Sha256(content);
        if (sha256 != source.Sha256) return FileErrors.HashMismatch;
        var prepared = await attachments.PrepareAsync(content, source.FileName ?? string.Empty, ct);
        if (prepared.Spreadsheet is not { } spreadsheet)
            return WhatsAppPreparedSpreadsheet.Rejected(prepared.Rejection ?? StructuredAttachmentRejection.Unavailable);
        var key = Key(source.MessageId, spreadsheet.Kind);
        await using (var stream = new MemoryStream(content, false)) await files.WriteOnceAsync(key, stream, spreadsheet.MediaType, ct);
        // Another host, or an attempt that did not commit, may have written it first: the copy is read back, never assumed ours.
        await using var stored = await files.OpenReadAsync(key, ct);
        if (stored is null) return FileErrors.Unavailable;
        var copy = await ReadBoundedAsync(stored, ct);
        return copy is not null && Sha256(copy) == sha256
            ? WhatsAppPreparedSpreadsheet.Stored(key, content.LongLength, sha256, spreadsheet) : FileErrors.HashMismatch;
    }

    private static async Task<byte[]?> ReadBoundedAsync(Stream stream, CancellationToken ct)
    {
        using var buffer = new MemoryStream();
        var chunk = new byte[81920];
        while (true)
        {
            var count = await stream.ReadAsync(chunk, ct);
            if (count == 0) return buffer.ToArray();
            if (buffer.Length + count > AssistantAttachmentInput.MaximumBytes) return null;
            buffer.Write(chunk, 0, count);
        }
    }

    private static string Sha256(byte[] content) => Convert.ToHexStringLower(SHA256.HashData(content));
}

/// <summary>
/// What came of downloading a spreadsheet: its private copy, with the size and SHA-256 of its bytes and the sheet its owner prepared,
/// or the reason it is not read.
/// </summary>
internal sealed record WhatsAppPreparedSpreadsheet(string? StorageKey, long Size, string? Sha256, StructuredSpreadsheet? Spreadsheet,
    StructuredAttachmentRejection? Rejection)
{
    public static WhatsAppPreparedSpreadsheet Stored(string storageKey, long size, string sha256, StructuredSpreadsheet spreadsheet) =>
        new(storageKey, size, sha256, spreadsheet, null);

    public static WhatsAppPreparedSpreadsheet Rejected(StructuredAttachmentRejection rejection) => new(null, 0, null, null, rejection);

    public override string ToString() => Rejection is { } rejection
        ? $"WhatsAppPreparedSpreadsheet {{ Rejection = {rejection} }}" : "WhatsAppPreparedSpreadsheet { Content = [REDACTED] }";
}
