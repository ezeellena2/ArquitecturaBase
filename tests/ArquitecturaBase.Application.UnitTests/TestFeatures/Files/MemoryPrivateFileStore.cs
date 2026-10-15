using System.Runtime.CompilerServices;
using ArquitecturaBase.Application.Models.Files;
using ArquitecturaBase.Application.Interfaces.Integrations.Files;

namespace ArquitecturaBase.Application.UnitTests.TestFeatures.Files;

internal sealed class MemoryPrivateFileStore(bool available = true) : IPrivateFileStore
{
    private readonly Dictionary<string, byte[]> content = new(StringComparer.Ordinal);
    public Dictionary<string, DateTime> Modified { get; } = new(StringComparer.Ordinal);
    public int WriteCount { get; private set; }
    public int ReadCount { get; private set; }
    public Action? BeforeRead { get; set; }
    public Stream? LastOpenedStream { get; private set; }
    public bool IsAvailable => available;
    public async Task WriteAsync(string key, Stream value, string contentType, CancellationToken cancellationToken)
    {
        WriteCount++;
        using var buffer = new MemoryStream();
        await value.CopyToAsync(buffer, cancellationToken);
        content[key] = buffer.ToArray();
        Modified[key] = DateTime.MinValue;
    }
    public Task<Stream> OpenReadAsync(string key, CancellationToken cancellationToken)
    { ReadCount++; BeforeRead?.Invoke(); LastOpenedStream = new MemoryStream(content[key], false); return Task.FromResult(LastOpenedStream); }
    public Task DeleteAsync(string key, CancellationToken cancellationToken) { content.Remove(key); Modified.Remove(key); return Task.CompletedTask; }
    public async IAsyncEnumerable<PrivateFileObjectRow> ListAsync(DateTime olderThanUtc, [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        await Task.CompletedTask;
        foreach (var row in Modified.ToArray()) if (row.Value < olderThanUtc) yield return new(row.Key, row.Value);
    }
}
