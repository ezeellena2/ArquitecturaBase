using System.Globalization;
using System.Resources;
using ArquitecturaBase.Application.Common.Formatting;

namespace ArquitecturaBase.Infrastructure.Emails.Resources;

/// <summary>Textos de Emails.resx en el idioma del destinatario (el Culture de su perfil).</summary>
internal static class EmailTexts
{
    internal static ResourceManager ResourceManager { get; } =
        new("ArquitecturaBase.Infrastructure.Emails.Resources.Emails", typeof(EmailTexts).Assembly);

    public static string Get(string key, CultureInfo culture) =>
        ResourceManager.GetString(key, culture) ?? throw new InvalidOperationException($"Missing email text '{key}'.");

    public static string Format(string key, CultureInfo culture, params object[] arguments) =>
        DisplayNumbers.Format(culture.Name, Get(key, culture), arguments);
}
