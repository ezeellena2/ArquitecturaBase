using ArquitecturaBase.Application.Models.Emails;
using System.Globalization;

namespace ArquitecturaBase.Application.Interfaces.Integrations.Emails;

/// <summary>
/// Arma los correos con el nombre de la aplicación y el logo de Configuración › Apariencia (RC6-B), leídos en cada correo.
/// </summary>
public interface IEmailTemplateRenderer
{
    /// <summary>Email con el código de ingreso, con los textos en <paramref name="culture"/>.</summary>
    Task<EmailMessage> RenderLoginCodeAsync(string to, string code, int lifetimeMinutes, CultureInfo culture, CancellationToken cancellationToken);

    /// <summary>
    /// Email con el código para agregar la dirección a una cuenta desde el perfil (sección 12 del spec del ingreso con
    /// WhatsApp): la misma plantilla que el de ingreso, con textos que dicen para qué es. El asunto también empieza con
    /// el código.
    /// </summary>
    Task<EmailMessage> RenderEmailVerificationCodeAsync(string to, string code, int lifetimeMinutes, CultureInfo culture, CancellationToken cancellationToken);

    /// <summary>
    /// Email con el código que confirma, desde el perfil, desvincular el número de la cuenta (cada medio confirma los
    /// cambios del otro). Va al correo verificado de la cuenta y dice para qué es: si no lo pidió la persona, alguien
    /// está usando su sesión.
    /// </summary>
    Task<EmailMessage> RenderPhoneUnlinkCodeAsync(string to, string code, int lifetimeMinutes, CultureInfo culture, CancellationToken cancellationToken);

    /// <summary>
    /// Email con el código que confirma, desde el perfil, vincular un número a la cuenta (o reemplazar uno sin
    /// verificar), como <see cref="RenderPhoneUnlinkCodeAsync"/>.
    /// </summary>
    Task<EmailMessage> RenderPhoneLinkCodeAsync(string to, string code, int lifetimeMinutes, CultureInfo culture, CancellationToken cancellationToken);

    /// <summary>
    /// Email con la invitación de un administrador (sección 6.6 del spec del ingreso con WhatsApp): saluda por
    /// <paramref name="displayName"/>, si hay, y lleva un botón a <paramref name="loginUrl"/>. No lleva nada que sirva
    /// para entrar: la persona entra con el código de siempre.
    /// </summary>
    Task<EmailMessage> RenderInvitationAsync(string to, string? displayName, string loginUrl, CultureInfo culture, CancellationToken cancellationToken);
}
