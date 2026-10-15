using System.Globalization;
using System.Text;
using ArquitecturaBase.Application.Interfaces.Integrations.Files;
using ArquitecturaBase.Application.Models.Files;
using ArquitecturaBase.Application.Models.StructuredData;
using ArquitecturaBase.Application.Modules.BusinessForms.Configuration;
using ArquitecturaBase.Domain.Common;
using ArquitecturaBase.Domain.Files;
using ArquitecturaBase.Domain.Results;
using Microsoft.Extensions.Options;

namespace ArquitecturaBase.Application.Modules.BusinessForms.Services;

internal sealed class BusinessFormFileRecorder(IPrivateFileStore store, IFileContentProcessor processor, IOptions<BusinessFormsOptions> options)
{
    public async Task<Result<IReadOnlyList<PreparedFormFile>>> PrepareAsync(StructuredRecordCaptureRequest request, CancellationToken cancellationToken)
    {
        var files = request.Files ?? [];
        if (files.Count == 0) return Array.Empty<PreparedFormFile>();
        if (!store.IsAvailable) return FileErrors.Unavailable;
        if (files.Count > 10 || files.Sum(file => file.Content?.LongLength ?? 0) > 100L * 1024 * 1024) return FileErrors.TooLarge;
        if (files.GroupBy(file => file.FieldId).Any(group => !group.Select(file => file.Position).Order().SequenceEqual(Enumerable.Range(0, group.Count()))))
            return FileErrors.InvalidMetadata;
        var verified = new List<(StructuredRecordFileInput Input, ProcessedPrivateFile Content, string Name)>();
        foreach (var input in files)
        {
            if (input.FieldId == Guid.Empty || input.Type is not ("photo" or "document") || input.Content is null
                || string.IsNullOrWhiteSpace(input.Name) || input.Name.Length > 255 || input.Name.Any(char.IsControl)
                || input.Name.Contains('/') || input.Name.Contains('\\') || !StorableText.IsStorable(input.Name.AsSpan())) return FileErrors.AtField(FileErrors.InvalidMetadata, input.FieldId);
            var result = processor.Process(input.Content, input.ContentType, input.Sha256, options.Value.MaxFileSizeMiB * 1024L * 1024, input.Type == "photo");
            if (result.IsFailure) return FileErrors.AtField(result.Error, input.FieldId);
            var name = DownloadName(input.Name, input.ContentType);
            if (name.Length is 0 or > 255) return FileErrors.AtField(FileErrors.InvalidMetadata, input.FieldId);
            verified.Add((input, result.Value, name));
        }
        var prepared = new List<PreparedFormFile>();
        foreach (var (input, content, name) in verified)
        {
            // A changed replay has a different hash/key and cannot overwrite an already confirmed original.
            var key = "responses/" + request.SourceReceiptId.ToString("N") + "/" + input.FieldId.ToString("N") + "/"
                + input.Position.ToString(CultureInfo.InvariantCulture) + "-" + content.Sha256;
            await using var original = new MemoryStream(input.Content!, false);
            await store.WriteAsync(key, original, input.ContentType, cancellationToken);
            string? thumbnailKey = null;
            if (content.Thumbnail is { } bytes)
            {
                thumbnailKey = key + ".thumb.jpg";
                await using var thumbnail = new MemoryStream(bytes, false);
                await store.WriteAsync(thumbnailKey, thumbnail, "image/jpeg", cancellationToken);
            }
            prepared.Add(new(input.FieldId, input.Position, input.Type, StoredFile.Create(request.ActorUserId, name,
                input.ContentType, input.Content!.LongLength, content.Sha256, key, thumbnailKey)));
        }
        return prepared;
    }
    private static string DownloadName(string original, string contentType)
    {
        var extension = contentType switch
        {
            "image/jpeg" => ".jpg", "image/png" => ".png", "application/pdf" => ".pdf",
            "application/msword" => ".doc", "application/vnd.ms-excel" => ".xls",
            "application/vnd.openxmlformats-officedocument.wordprocessingml.document" => ".docx",
            "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet" => ".xlsx", _ => ""
        };
        if (extension.Length == 0) return "";
        var clean = new StringBuilder();
        foreach (var rune in original.Normalize(NormalizationForm.FormC).EnumerateRunes())
            if (Rune.GetUnicodeCategory(rune) != UnicodeCategory.Format && !"<>:\"/\\|?*".Contains(rune.ToString(), StringComparison.Ordinal))
                clean.Append(rune);
        var stem = Path.GetFileNameWithoutExtension(clean.ToString()).Trim(' ', '.');
        return (stem.Length == 0 ? "file" : stem) + extension;
    }

}

internal sealed record PreparedFormFile(Guid FieldId, int Position, string Type, StoredFile File)
{
    public override string ToString() => "PreparedFormFile { Content = [REDACTED] }";
}
