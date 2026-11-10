using System.Collections.Concurrent;
using System.Runtime.CompilerServices;
using ArquitecturaBase.Application.Interfaces.Integrations.Files;
using ArquitecturaBase.Application.Models.Files;

namespace ArquitecturaBase.Api.IntegrationTests.TestFeatures.Modules.BusinessForms;

// In-memory private storage for the copies of the assistant's spreadsheets that a test hands out: the import reads the copy of a
// proposal's turn from here, as it would from the real store.
internal sealed class AssistantCopyFiles : IPrivateFileStore
{
    private readonly ConcurrentDictionary<string, byte[]> content = new(StringComparer.Ordinal);
    private int reads;

    public bool IsAvailable => true;
    public int Reads => Volatile.Read(ref reads);

    public void Put(string key, byte[] bytes) => content[key] = bytes.ToArray();

    public async Task<bool> WriteOnceAsync(string key, Stream value, string contentType, CancellationToken cancellationToken)
    {
        using var buffer = new MemoryStream();
        await value.CopyToAsync(buffer, cancellationToken);
        return content.TryAdd(key, buffer.ToArray());
    }

    public async Task WriteAsync(string key, Stream value, string contentType, CancellationToken cancellationToken)
    {
        using var buffer = new MemoryStream();
        await value.CopyToAsync(buffer, cancellationToken);
        content[key] = buffer.ToArray();
    }

    public Task<Stream?> OpenReadAsync(string key, CancellationToken cancellationToken)
    {
        Interlocked.Increment(ref reads);
        return Task.FromResult<Stream?>(content.TryGetValue(key, out var bytes) ? new MemoryStream(bytes, false) : null);
    }

    public Task DeleteAsync(string key, CancellationToken cancellationToken)
    {
        content.TryRemove(key, out _);
        return Task.CompletedTask;
    }

    public async IAsyncEnumerable<PrivateFileObjectRow> ListAsync(DateTime olderThanUtc, [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        await Task.CompletedTask;
        yield break;
    }
}
