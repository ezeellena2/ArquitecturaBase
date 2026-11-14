using ArquitecturaBase.Application.Modules.WhatsApp.Models;
using ArquitecturaBase.Infrastructure.Delivery;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace ArquitecturaBase.Infrastructure.Modules.WhatsApp;

internal sealed class WhatsAppMessageSource(IServiceScopeFactory scopeFactory, IDataProtectionProvider protection, ILogger<WhatsAppMessageSource> logger)
    : DurableMessageSource<WhatsAppOutboundMessage>(scopeFactory, protection, logger)
{
    protected override string Channel => "whatsapp-access";
    protected override WhatsAppOutboundMessage Deserialize(string messageType, string payload) => WhatsAppQueuedMessageCodec.Deserialize(messageType, payload);

    // H3: a message that waits for its turn knows when it stops being worth sending (a code, after its ten minutes).
    protected override WhatsAppOutboundMessage WithExpiry(WhatsAppOutboundMessage message, DateTime? expiresAtUtc) =>
        expiresAtUtc is null ? message : message with { ExpiresAtUtc = expiresAtUtc };
}
