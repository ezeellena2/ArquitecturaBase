using ArquitecturaBase.Application.Modules.WhatsApp.Models;
using ArquitecturaBase.Infrastructure.Modules.WhatsApp;

namespace ArquitecturaBase.Api.IntegrationTests.TestFeatures.Modules.WhatsApp;

internal sealed class CapturingWhatsAppCloudClient : IWhatsAppCloudClient
{
    public List<WhatsAppOutboundMessage> Messages { get; } = [];

    public Task<WhatsAppSendResult> SendAsync(WhatsAppOutboundMessage message, CancellationToken cancellationToken)
    {
        Messages.Add(message);
        return Task.FromResult(WhatsAppSendResult.Sent("wamid.captured-flow"));
    }
}
