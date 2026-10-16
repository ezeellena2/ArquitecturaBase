namespace ArquitecturaBase.Application.Interfaces.Channels;
/// <summary>Optional durable references to private objects before their metadata is confirmed. Read-only and batched.</summary>
public interface IPrivateFileRetentionSource
{
    Task<IReadOnlyList<string>> ListReferencedKeysAsync(IReadOnlyList<string> storageKeys, CancellationToken cancellationToken);
}
