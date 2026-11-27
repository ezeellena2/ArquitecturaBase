using System.Collections.Concurrent;
using ArquitecturaBase.Application.Interfaces.Integrations.Files;
using ArquitecturaBase.Application.Models.Files;

namespace ArquitecturaBase.Api.IntegrationTests.TestFeatures;

/// <summary>ME2: a private store in memory for the logo and the icon; writing once keeps the first copy, like Azure.</summary>
internal sealed class MemoryBrandFiles : IPrivateFileStore
{
    private readonly ConcurrentDictionary<string, byte[]> objects = new(StringComparer.Ordinal);

    public bool IsAvailable => true;

    public IReadOnlyDictionary<string, byte[]> Objects => objects;

    // Lists nothing, so the cleanup never deletes through it.
    public async IAsyncEnumerable<PrivateFileObjectRow> ListAsync(DateTime olderThanUtc,
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken)
    {
        await Task.CompletedTask;
        yield break;
    }

    public async Task<bool> WriteOnceAsync(string key, Stream content, string contentType, CancellationToken cancellationToken)
    {
        using var buffer = new MemoryStream();
        await content.CopyToAsync(buffer, cancellationToken);
        return objects.TryAdd(key, buffer.ToArray());
    }

    public Task WriteAsync(string key, Stream content, string contentType, CancellationToken cancellationToken) =>
        throw new NotSupportedException();

    public Task<Stream?> OpenReadAsync(string key, CancellationToken cancellationToken) =>
        Task.FromResult<Stream?>(objects.TryGetValue(key, out var bytes) ? new MemoryStream(bytes, writable: false) : null);

    public Task DeleteAsync(string key, CancellationToken cancellationToken) => throw new NotSupportedException();

    public Task DeleteIfUnchangedAsync(string key, string version, CancellationToken cancellationToken) => throw new NotSupportedException();
}
