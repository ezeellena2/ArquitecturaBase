using System.Runtime.CompilerServices;
using ArquitecturaBase.Application.Models.Files;
using ArquitecturaBase.Application.Interfaces.Integrations.Files;

namespace ArquitecturaBase.Application.UnitTests.TestFeatures.Files;

internal sealed class MemoryPrivateFileStore(bool available = true) : IPrivateFileStore
{
    private readonly Dictionary<string, byte[]> content = new(StringComparer.Ordinal);
    public Dictionary<string, DateTime> Modified { get; } = new(StringComparer.Ordinal);
    private readonly Dictionary<string, int> versions = new(StringComparer.Ordinal);
    public int WriteCount { get; private set; }
    public int ReadCount { get; private set; }
    public Action? BeforeRead { get; set; }
    public Stream? LastOpenedStream { get; private set; }
    public bool IsAvailable => available;
    public async Task WriteAsync(string key, Stream value, string contentType, CancellationToken cancellationToken)
    {
        using var buffer = new MemoryStream(); await value.CopyToAsync(buffer, cancellationToken);
        lock (content)
        {
            content[key] = buffer.ToArray(); Modified[key] = DateTime.MinValue; WriteCount++; versions[key] = WriteCount;
        }
    }
    public async Task<bool> WriteOnceAsync(string key, Stream value, string contentType, CancellationToken cancellationToken)
    {
        using var buffer = new MemoryStream(); await value.CopyToAsync(buffer, cancellationToken);
        lock (content)
        {
            if (content.ContainsKey(key)) return false;
            content.Add(key, buffer.ToArray()); Modified[key] = DateTime.MinValue; WriteCount++; versions[key] = WriteCount; return true;
        }
    }
    public Task<Stream?> OpenReadAsync(string key, CancellationToken cancellationToken)
    {
        BeforeRead?.Invoke();
        lock (content)
        {
            ReadCount++; LastOpenedStream = content.TryGetValue(key, out var bytes) ? new MemoryStream(bytes, false) : null;
            return Task.FromResult<Stream?>(LastOpenedStream);
        }
    }
    public Task DeleteAsync(string key, CancellationToken cancellationToken)
    {
        lock (content) { content.Remove(key); Modified.Remove(key); versions.Remove(key); } return Task.CompletedTask;
    }
    public Task DeleteIfUnchangedAsync(string key, string version, CancellationToken cancellationToken)
    {
        lock (content)
            if (versions.TryGetValue(key, out var current) && current.ToString(System.Globalization.CultureInfo.InvariantCulture) == version)
            {
                content.Remove(key); Modified.Remove(key); versions.Remove(key);
            }
        return Task.CompletedTask;
    }
    public async IAsyncEnumerable<PrivateFileObjectRow> ListAsync(DateTime olderThanUtc, [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        await Task.CompletedTask; (string Key, DateTime Modified, int Version)[] rows;
        lock (content) rows = Modified.Select(row => (row.Key, row.Value, versions[row.Key])).ToArray();
        foreach (var row in rows)
            if (row.Modified < olderThanUtc) yield return new(row.Key, row.Modified, row.Version.ToString(System.Globalization.CultureInfo.InvariantCulture));
    }
}
