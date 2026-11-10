using System.Runtime.CompilerServices;
using ArquitecturaBase.Application.Interfaces.Integrations.Files;
using ArquitecturaBase.Application.Models.Files;
using ArquitecturaBase.Application.Modules.WhatsApp.Interfaces.Integrations;
using ArquitecturaBase.Application.Modules.WhatsApp.Models;
using ArquitecturaBase.Domain.Results;

namespace ArquitecturaBase.Api.IntegrationTests.TestFeatures.Modules.WhatsApp;

// Stage 2 of the assistant authoring: only Meta's media download and the private storage of a received spreadsheet are replaced, in
// memory and with fictitious bytes. The webhook, the processor, the dispatch, the owner's reader and the transaction stay productive.
internal sealed class WhatsAppDocumentProbe : IPrivateFileStore, IWhatsAppMediaClient
{
    private readonly Dictionary<string, byte[]> files = new(StringComparer.Ordinal);
    public byte[] Bytes { get; set; } = [];
    public int Downloads => Limits.Count;
    public List<long> Limits { get; } = [];
    public bool IsAvailable => true;
    public IReadOnlyCollection<string> Keys => files.Keys;
    public byte[]? Stored(string key) => files.GetValueOrDefault(key);

    public Task<Result<WhatsAppMediaContent>> DownloadAsync(string mediaId, string contentType, string sha256, long maximumBytes, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        Limits.Add(maximumBytes);
        return Task.FromResult(Result.Success(new WhatsAppMediaContent(Bytes)));
    }

    public async Task<bool> WriteOnceAsync(string key, Stream content, string contentType, CancellationToken ct)
    {
        if (files.ContainsKey(key)) return false;
        using var buffer = new MemoryStream();
        await content.CopyToAsync(buffer, ct);
        files.Add(key, buffer.ToArray());
        return true;
    }

    public Task WriteAsync(string key, Stream content, string contentType, CancellationToken ct) => throw new NotSupportedException();

    public Task<Stream?> OpenReadAsync(string key, CancellationToken ct) =>
        Task.FromResult<Stream?>(files.TryGetValue(key, out var bytes) ? new MemoryStream(bytes, false) : null);

    public Task DeleteAsync(string key, CancellationToken ct)
    {
        files.Remove(key);
        return Task.CompletedTask;
    }

    public async IAsyncEnumerable<PrivateFileObjectRow> ListAsync(DateTime olderThanUtc, [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        await Task.CompletedTask;
        yield break;
    }
}
