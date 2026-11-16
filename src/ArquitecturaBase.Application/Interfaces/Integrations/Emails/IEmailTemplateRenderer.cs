using ArquitecturaBase.Application.Models.Emails;
using System.Globalization;

namespace ArquitecturaBase.Application.Interfaces.Integrations.Emails;

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
    /// Email con el código que confirma, desde el perfil, desvincular el número de la cuenta (cada medio confirma los
    /// cambios del otro). Va al correo verificado de la cuenta y dice para qué es: si no lo pidió la persona, alguien
    /// está usando su sesión.
    /// </summary>
    EmailMessage RenderPhoneUnlinkCode(string to, string code, int lifetimeMinutes, CultureInfo culture);

    /// <summary>
    /// Email con el código que confirma, desde el perfil, vincular un número a la cuenta (o reemplazar uno sin
    /// verificar), como <see cref="RenderPhoneUnlinkCode"/>.
    /// </summary>
    EmailMessage RenderPhoneLinkCode(string to, string code, int lifetimeMinutes, CultureInfo culture);

    /// <summary>
    /// Email con la invitación de un administrador (sección 6.6 del spec del ingreso con WhatsApp): saluda por
    /// <paramref name="displayName"/>, si hay, y lleva un botón a <paramref name="loginUrl"/>. No lleva nada que sirva
    /// para entrar: la persona entra con el código de siempre.
    /// </summary>
    EmailMessage RenderInvitation(string to, string? displayName, string loginUrl, CultureInfo culture);
}
