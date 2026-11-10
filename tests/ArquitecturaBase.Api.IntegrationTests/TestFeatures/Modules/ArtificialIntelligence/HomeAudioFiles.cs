using System.Collections.Concurrent;
using System.Runtime.CompilerServices;
using ArquitecturaBase.Application.Interfaces.Integrations.Files;
using ArquitecturaBase.Application.Models.Files;

namespace ArquitecturaBase.Api.IntegrationTests.TestFeatures.Modules.ArtificialIntelligence;

// In-memory private storage for Home recordings and spreadsheets: WriteOnce keeps the first copy, as the real store does.
internal sealed class HomeAudioFiles : IPrivateFileStore
{
    private readonly ConcurrentDictionary<string, byte[]> content = new(StringComparer.Ordinal);
    public bool IsAvailable => true;
    public IReadOnlyCollection<string> Keys => content.Keys.ToArray();
    public async Task<bool> WriteOnceAsync(string key, Stream value, string contentType, CancellationToken ct)
    {
        using var buffer = new MemoryStream();
        await value.CopyToAsync(buffer, ct);
        return content.TryAdd(key, buffer.ToArray());
    }
    public async Task WriteAsync(string key, Stream value, string contentType, CancellationToken ct)
    {
        using var buffer = new MemoryStream();
        await value.CopyToAsync(buffer, ct);
        content[key] = buffer.ToArray();
    }
    public Task<Stream?> OpenReadAsync(string key, CancellationToken ct) =>
        Task.FromResult<Stream?>(content.TryGetValue(key, out var bytes) ? new MemoryStream(bytes, false) : null);
    public Task DeleteAsync(string key, CancellationToken ct)
    {
        content.TryRemove(key, out _);
        return Task.CompletedTask;
    }
    public async IAsyncEnumerable<PrivateFileObjectRow> ListAsync(DateTime olderThanUtc, [EnumeratorCancellation] CancellationToken ct)
    {
        await Task.CompletedTask;
        yield break;
    }
}
