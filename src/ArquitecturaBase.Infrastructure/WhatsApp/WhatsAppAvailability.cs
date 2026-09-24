using ArquitecturaBase.Application.Interfaces.Integrations;

namespace ArquitecturaBase.Infrastructure.WhatsApp;

/// <summary>
/// Se decide una vez, al registrar los servicios: WhatsApp, según haya o no <c>WhatsApp:PhoneNumberId</c>, y el webhook,
/// según estén además sus dos secretos.
/// </summary>
internal sealed record WhatsAppAvailability(bool IsEnabled, bool IsWebhookEnabled = false) : IWhatsAppAvailability;
