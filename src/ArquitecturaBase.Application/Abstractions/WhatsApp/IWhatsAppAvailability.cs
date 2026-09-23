namespace ArquitecturaBase.Application.Abstractions.WhatsApp;

/// <summary>
/// Si WhatsApp está configurado. Sin <c>WhatsApp:PhoneNumberId</c> queda apagado y la app arranca igual, como Google
/// sin su ClientId: el ingreso con WhatsApp no se ofrece y sus endpoints responden 404.
/// </summary>
public interface IWhatsAppAvailability
{
    bool IsEnabled { get; }
}
