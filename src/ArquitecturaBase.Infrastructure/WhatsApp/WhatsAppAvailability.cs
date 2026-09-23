using ArquitecturaBase.Application.Abstractions.WhatsApp;

namespace ArquitecturaBase.Infrastructure.WhatsApp;

/// <summary>Se decide una vez, al registrar los servicios, según haya o no <c>WhatsApp:PhoneNumberId</c>.</summary>
internal sealed record WhatsAppAvailability(bool IsEnabled) : IWhatsAppAvailability;
