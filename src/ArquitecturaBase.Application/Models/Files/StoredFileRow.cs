namespace ArquitecturaBase.Application.Models.Files;

public sealed record StoredFileRow(Guid Id, Guid OwnerUserId, string Name, string ContentType, long Size,
    string Sha256, string StorageKey, string? ThumbnailKey)
{
    public override string ToString() => "StoredFileRow { Content = [REDACTED] }";
}
/// <summary><c>Version</c> is the provider's opaque version of the listed object (the ETag in Azure).</summary>
public sealed record PrivateFileObjectRow(string Key, DateTime LastModifiedAtUtc, string Version)
{
    public override string ToString() => "PrivateFileObjectRow { Content = [REDACTED] }";
}
public sealed record ProcessedPrivateFile(string Sha256, byte[]? Thumbnail)
{
    public override string ToString() => "ProcessedPrivateFile { Content = [REDACTED] }";
}
