
using ArquitecturaBase.Application.Modules.WhatsApp.Models;

namespace ArquitecturaBase.Infrastructure.Modules.WhatsApp;

/// <summary>Manda un mensaje a la Graph API de WhatsApp. Lo usa la cola; los casos de uso encolan con IWhatsAppSendQueue.</summary>
internal interface IWhatsAppCloudClient
{
    Task<WhatsAppSendResult> SendAsync(WhatsAppOutboundMessage message, CancellationToken cancellationToken);
}
