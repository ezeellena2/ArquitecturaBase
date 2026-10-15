using ArquitecturaBase.Application.Interfaces.Integrations.Files;

namespace ArquitecturaBase.Application.UnitTests.TestFeatures.Files;

internal sealed class MemoryPrivateFileStore(bool available = true) : IPrivateFileStore
{
    private readonly Dictionary<string, byte[]> content = new(StringComparer.Ordinal);
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
    }
    public Task<Stream> OpenReadAsync(string key, CancellationToken cancellationToken)
    { ReadCount++; BeforeRead?.Invoke(); LastOpenedStream = new MemoryStream(content[key], false); return Task.FromResult(LastOpenedStream); }
    public Task DeleteAsync(string key, CancellationToken cancellationToken) { content.Remove(key); return Task.CompletedTask; }
}
