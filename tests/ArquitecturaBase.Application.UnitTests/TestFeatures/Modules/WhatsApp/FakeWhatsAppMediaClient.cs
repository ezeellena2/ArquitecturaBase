using ArquitecturaBase.Application.Modules.WhatsApp.Interfaces.Integrations;
using ArquitecturaBase.Application.Modules.WhatsApp.Models;
using ArquitecturaBase.Domain.Results;
namespace ArquitecturaBase.Application.UnitTests.TestFeatures.Modules.WhatsApp;
internal sealed class FakeWhatsAppMediaClient : IWhatsAppMediaClient
{
    public Result<WhatsAppMediaContent> Result { get; set; } = new WhatsAppMediaContent([1, 2, 3]);
    public Action<CancellationToken>? OnDownload { get; set; }
    public int DownloadCount { get; private set; }
    public Task<Result<WhatsAppMediaContent>> DownloadAsync(string mediaId, string contentType, string sha256, CancellationToken cancellationToken)
    { DownloadCount++; OnDownload?.Invoke(cancellationToken); cancellationToken.ThrowIfCancellationRequested(); return Task.FromResult(Result); }
}
