using ArquitecturaBase.Domain.Common;

namespace ArquitecturaBase.Domain.Files;

public sealed class StoredFile : Entity, IAuditable
{
    private StoredFile() { }
    public Guid OwnerUserId { get; private set; }
    public string Name { get; private set; } = "";
    public string ContentType { get; private set; } = "";
    public long Size { get; private set; }
    public string Sha256 { get; private set; } = "";
    public string StorageKey { get; private set; } = "";
    public string? ThumbnailKey { get; private set; }
    public DateTime CreatedAtUtc { get; private set; }
    public Guid? CreatedBy { get; private set; }
    public DateTime? ModifiedAtUtc { get; private set; }
    public Guid? ModifiedBy { get; private set; }

    public static StoredFile Create(Guid ownerUserId, string name, string contentType, long size, string sha256, string storageKey, string? thumbnailKey)
    {
        ArgumentOutOfRangeException.ThrowIfEqual(ownerUserId, Guid.Empty);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(size);
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentException.ThrowIfNullOrWhiteSpace(storageKey);
        return new() { OwnerUserId = ownerUserId, Name = name, ContentType = contentType,
            Size = size, Sha256 = sha256, StorageKey = storageKey, ThumbnailKey = thumbnailKey };
    }

    public override string ToString() => "StoredFile { Content = [REDACTED] }";
}
