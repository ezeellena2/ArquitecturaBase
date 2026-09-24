namespace ArquitecturaBase.Application.Interfaces.Integrations;

/// <summary>
/// Si el ingreso con Google está configurado. Sin <c>Authentication:Google:ClientId</c> Google no se registra y la
/// pantalla de login no lo ofrece, igual que WhatsApp sin su PhoneNumberId (<c>IWhatsAppAvailability</c>).
/// </summary>
public interface IGoogleAvailability
{
    bool IsEnabled { get; }
}
