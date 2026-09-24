using ArquitecturaBase.Application.Models.Emails;
using System.Globalization;

namespace ArquitecturaBase.Application.Interfaces.Integrations;

public interface IEmailTemplateRenderer
{
    /// <summary>Email con el código de ingreso, con los textos en <paramref name="culture"/>.</summary>
    EmailMessage RenderLoginCode(string to, string code, int lifetimeMinutes, CultureInfo culture);

    /// <summary>
    /// Email con el código para agregar la dirección a una cuenta desde el perfil (sección 12 del spec del ingreso con
    /// WhatsApp): la misma plantilla que el de ingreso, con textos que dicen para qué es. El asunto también empieza con
    /// el código.
    /// </summary>
    EmailMessage RenderEmailVerificationCode(string to, string code, int lifetimeMinutes, CultureInfo culture);

    /// <summary>
    /// Email con la invitación de un administrador (sección 6.6 del spec del ingreso con WhatsApp): saluda por
    /// <paramref name="displayName"/>, si hay, y lleva un botón a <paramref name="loginUrl"/>. No lleva nada que sirva
    /// para entrar: la persona entra con el código de siempre.
    /// </summary>
    EmailMessage RenderInvitation(string to, string? displayName, string loginUrl, CultureInfo culture);
}
