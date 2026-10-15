using ArquitecturaBase.Application.Modules.WhatsApp.Interfaces.Integrations;
using ArquitecturaBase.Application.Modules.WhatsApp.Models;
using ArquitecturaBase.Domain.Results;

namespace ArquitecturaBase.Application.Modules.WhatsApp.Services;

internal sealed class WhatsAppFlowMediaRecorder(IWhatsAppMediaClient client, TimeProvider timeProvider)
{
    public async Task WithinDeadlineAsync(Func<CancellationToken, Task> capture, CancellationToken cancellationToken)
    {
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(90), timeProvider);
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, deadline.Token);
        await capture(linked.Token);
    }

    public Task<Result<WhatsAppMediaContent>> DownloadAsync(string mediaId, string contentType, string sha256, CancellationToken cancellationToken) =>
        client.DownloadAsync(mediaId, contentType, sha256, cancellationToken);
}
