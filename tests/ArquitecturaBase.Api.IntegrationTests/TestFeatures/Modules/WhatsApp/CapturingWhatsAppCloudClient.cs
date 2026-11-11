using ArquitecturaBase.Application.Modules.WhatsApp.Models;
using ArquitecturaBase.Infrastructure.Modules.WhatsApp;

namespace ArquitecturaBase.Api.IntegrationTests.TestFeatures.Modules.WhatsApp;

/// <summary>
/// Captures what would go to Meta. By default every message gets the same id; <paramref name="uniqueIds"/> gives each its own, as
/// Meta does, for a scenario that sends more than one message and reads them back in the chat (W1-A).
/// </summary>
internal sealed class CapturingWhatsAppCloudClient(bool uniqueIds = false) : IWhatsAppCloudClient
{
    public List<WhatsAppOutboundMessage> Messages { get; } = [];

    public Task<WhatsAppSendResult> SendAsync(WhatsAppOutboundMessage message, CancellationToken cancellationToken)
    {
        Messages.Add(message);
        return Task.FromResult(WhatsAppSendResult.Sent(uniqueIds ? "wamid.captured-flow-" + Messages.Count : "wamid.captured-flow"));
    }
}
