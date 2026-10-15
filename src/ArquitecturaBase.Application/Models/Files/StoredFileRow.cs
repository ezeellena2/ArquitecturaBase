namespace ArquitecturaBase.Application.Models.Files;

public sealed record StoredFileRow(Guid Id, Guid OwnerUserId, string Name, string ContentType, long Size,
    string Sha256, string StorageKey, string? ThumbnailKey)
{
    public override string ToString() => "StoredFileRow { Content = [REDACTED] }";
}
public sealed record PrivateFileObjectRow(string Key, DateTime LastModifiedAtUtc)
{
    public override string ToString() => "PrivateFileObjectRow { Content = [REDACTED] }";
}
public sealed record ProcessedPrivateFile(string Sha256, byte[]? Thumbnail)
{
    public override string ToString() => "ProcessedPrivateFile { Content = [REDACTED] }";
}
