using System.Globalization;

namespace ArquitecturaBase.Application.Abstractions.Emails;

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
}
