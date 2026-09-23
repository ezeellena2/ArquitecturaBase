using ArquitecturaBase.Application.Abstractions.WhatsApp;

namespace ArquitecturaBase.Infrastructure.WhatsApp;

/// <summary>Manda un mensaje a la Graph API de WhatsApp. Lo usa la cola; los casos de uso encolan con IWhatsAppOutbox.</summary>
internal interface IWhatsAppCloudClient
{
    Task<WhatsAppSendResult> SendAsync(WhatsAppOutboundMessage message, CancellationToken cancellationToken);
}
