using ArquitecturaBase.Application.Modules.WhatsApp.Models;
using ArquitecturaBase.Infrastructure.Modules.WhatsApp;

namespace ArquitecturaBase.Api.IntegrationTests.TestFeatures.Modules.WhatsApp;

/// <summary>
/// Captures what would go to Meta. By default every message gets the same id; <paramref name="uniqueIds"/> gives each its own, as
/// Meta does, for a scenario that sends more than one message and reads them back in the chat (W1-A).
/// </summary>
// A prefix of its own keeps the ids of one test apart from another's in the shared database (wamids are unique).
internal sealed class CapturingWhatsAppCloudClient(bool uniqueIds = false, string prefix = "wamid.captured-flow-") : IWhatsAppCloudClient
{
    public List<WhatsAppOutboundMessage> Messages { get; } = [];

    public Task<WhatsAppSendResult> SendAsync(WhatsAppOutboundMessage message, CancellationToken cancellationToken)
    {
        Messages.Add(message);
        return Task.FromResult(WhatsAppSendResult.Sent(uniqueIds ? prefix + Messages.Count : "wamid.captured-flow"));
    }
}
