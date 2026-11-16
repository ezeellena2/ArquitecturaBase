using ArquitecturaBase.Application.Models.Files;
namespace ArquitecturaBase.Application.Interfaces.Integrations.Files;

/// <summary>Private content only. Keys are server-generated; the caller owns authorization and metadata.</summary>
public interface IPrivateFileStore
{
    IAsyncEnumerable<PrivateFileObjectRow> ListAsync(DateTime olderThanUtc, CancellationToken cancellationToken);
    bool IsAvailable { get; }
    /// <summary>Creates the object only when absent. Returns true when created, false when an existing object is preserved.</summary>
    Task<bool> WriteOnceAsync(string key, Stream content, string contentType, CancellationToken cancellationToken);
    Task WriteAsync(string key, Stream content, string contentType, CancellationToken cancellationToken);
    Task<Stream?> OpenReadAsync(string key, CancellationToken cancellationToken);
    Task DeleteAsync(string key, CancellationToken cancellationToken);
    /// <summary>Deletes the object only while it keeps the version that <see cref="ListAsync"/> returned; a newer upload with the same key is preserved.</summary>
    Task DeleteIfUnchangedAsync(string key, string version, CancellationToken cancellationToken);
}
