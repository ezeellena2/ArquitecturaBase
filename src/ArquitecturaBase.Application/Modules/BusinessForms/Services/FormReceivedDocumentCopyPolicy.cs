using System.Security.Cryptography;
using ArquitecturaBase.Application.Interfaces.Integrations.Files;
using ArquitecturaBase.Application.Models.StructuredData;

namespace ArquitecturaBase.Application.Modules.BusinessForms.Services;

/// <summary>
/// The inbox's own copy of a received document (decision 9): <c>forms/documents/{id}.{ext}</c>, named after the document, written once
/// from the channel's private copy (checked by size and SHA-256 before) and read back, never assumed ours: another host or an attempt
/// that did not commit may have written it first. Outside any unit of work; a copy no live document keeps is cleaned up by the core.
/// </summary>
internal sealed class FormReceivedDocumentCopyPolicy(IPrivateFileStore files)
{
    public const string Prefix = "forms/documents/";
    // VC1-A: the copy of a spreadsheet, which its rows share.
    private static readonly string[] Extensions = [".pdf", ".jpg", ".png", ".docx", ".xlsx", ".csv", ".bin"];

    public bool IsAvailable => files.IsAvailable;

    public static string Key(Guid documentId, string mediaType) => $"{Prefix}{documentId:N}{Extension(mediaType)}";

    public static string Extension(string mediaType) => mediaType switch
    {
        "application/pdf" => ".pdf",
        "image/jpeg" => ".jpg",
        "image/png" => ".png",
        "application/vnd.openxmlformats-officedocument.wordprocessingml.document" => ".docx",
        FormReceivedDocumentRowPolicy.XlsxMediaType => ".xlsx",
        FormReceivedDocumentRowPolicy.CsvMediaType => ".csv",
        _ => ".bin",
    };

    public static bool TryParseKey(string key, out Guid documentId)
    {
        documentId = Guid.Empty;
        if (!key.StartsWith(Prefix, StringComparison.Ordinal) || key.Length <= Prefix.Length + 32) return false;
        var name = key[Prefix.Length..];
        return Extensions.Contains(name[32..], StringComparer.Ordinal) && Guid.TryParseExact(name[..32], "N", out documentId) && documentId != Guid.Empty;
    }

    /// <summary>The key of the copy, or null when the channel's copy is missing or not the file it describes, or the store is not there.</summary>
    public async Task<string?> CopyAsync(Guid documentId, StructuredDocumentCopy copy, CancellationToken ct)
    {
        if (!files.IsAvailable) return null;
        var content = await ReadAsync(copy.StorageKey, copy.Sha256, copy.Size, ct);
        if (content is null) return null;
        var key = Key(documentId, copy.MediaType);
        await using (var stream = new MemoryStream(content, false)) await files.WriteOnceAsync(key, stream, copy.MediaType, ct);
        return await ReadAsync(key, copy.Sha256, copy.Size, ct) is null ? null : key;
    }

    public async Task<Stream?> OpenAsync(string key, CancellationToken ct) => files.IsAvailable ? await files.OpenReadAsync(key, ct) : null;

    /// <summary>The bytes of a copy when they are the file described (size and SHA-256), within the limit of an attachment; null otherwise.</summary>
    public async Task<byte[]?> ReadAsync(string key, string sha256, long size, CancellationToken ct)
    {
        if (!files.IsAvailable || size is < 0 or > StructuredAttachmentLimits.MaximumBytes) return null;
        await using var stream = await files.OpenReadAsync(key, ct);
        if (stream is null) return null;
        using var buffer = new MemoryStream();
        var chunk = new byte[81920];
        while (true)
        {
            var count = await stream.ReadAsync(chunk, ct);
            if (count == 0) break;
            if (buffer.Length + count > StructuredAttachmentLimits.MaximumBytes) return null;
            buffer.Write(chunk, 0, count);
        }
        var bytes = buffer.ToArray();
        return bytes.LongLength == size && Convert.ToHexStringLower(SHA256.HashData(bytes)) == sha256 ? bytes : null;
    }
}
