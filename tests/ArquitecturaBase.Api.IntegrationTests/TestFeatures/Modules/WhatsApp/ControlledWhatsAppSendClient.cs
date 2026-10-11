using ArquitecturaBase.Application.Modules.WhatsApp.Models;
using ArquitecturaBase.Infrastructure.Modules.WhatsApp;

namespace ArquitecturaBase.Api.IntegrationTests.TestFeatures.Modules.WhatsApp;

internal sealed class ControlledWhatsAppSendClient : IWhatsAppCloudClient
{
    public WhatsAppSendResult NextResult { get; set; } = WhatsAppSendResult.Sent("wamid.fixture-" + Guid.NewGuid().ToString("N"));
    public int Sends { get; private set; }

    public Task<WhatsAppSendResult> SendAsync(WhatsAppOutboundMessage message, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        Sends++;
        return Task.FromResult(NextResult);
    }
}
