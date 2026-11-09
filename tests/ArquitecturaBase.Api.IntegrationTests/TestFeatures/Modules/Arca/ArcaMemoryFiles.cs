using System.Collections.Concurrent;
using ArquitecturaBase.Application.Interfaces.Integrations.Files;
using ArquitecturaBase.Application.Models.Files;

namespace ArquitecturaBase.Api.IntegrationTests.TestFeatures.Modules.Arca;

/// <summary>Un almacén privado en memoria para el PDF fiscal: escribir una vez conserva lo primero, como el de Azure.</summary>
internal sealed class ArcaMemoryFiles : IPrivateFileStore
{
    private readonly ConcurrentDictionary<string, byte[]> objects = new(StringComparer.Ordinal);

    public bool IsAvailable => true;

    public int Writes { get; private set; }

    public IReadOnlyDictionary<string, byte[]> Objects => objects;

    public async IAsyncEnumerable<PrivateFileObjectRow> ListAsync(DateTime olderThanUtc, [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken)
    {
        await Task.CompletedTask;
        yield break;
    }

    public async Task<bool> WriteOnceAsync(string key, Stream content, string contentType, CancellationToken cancellationToken)
    {
        Writes++;
        using var buffer = new MemoryStream();
        await content.CopyToAsync(buffer, cancellationToken);
        return objects.TryAdd(key, buffer.ToArray());
    }

    public Task WriteAsync(string key, Stream content, string contentType, CancellationToken cancellationToken) =>
        throw new NotSupportedException("The fiscal PDF is written once.");

    public Task<Stream?> OpenReadAsync(string key, CancellationToken cancellationToken) =>
        Task.FromResult<Stream?>(objects.TryGetValue(key, out var bytes) ? new MemoryStream(bytes) : null);

    public Task DeleteAsync(string key, CancellationToken cancellationToken)
    {
        objects.TryRemove(key, out _);
        return Task.CompletedTask;
    }
}
