using System.Globalization;

namespace ArquitecturaBase.Application.Abstractions.Emails;

public interface IEmailTemplateRenderer
{
    /// <summary>Email con el código de ingreso, con los textos en <paramref name="culture"/>.</summary>
    EmailMessage RenderLoginCode(string to, string code, int lifetimeMinutes, CultureInfo culture);
}
