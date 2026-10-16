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
}
