using System.Globalization;
using System.Resources;
using ArquitecturaBase.Application.Common.Formatting;

namespace ArquitecturaBase.Application.Modules.BusinessForms.Resources;

/// <summary>The texts of Configuración › Formularios (RC4-A), in Spanish and English.</summary>
internal static class FormSettingsTexts
{
    private static readonly ResourceManager Texts = new("ArquitecturaBase.Application.Modules.BusinessForms.Resources.Settings", typeof(FormSettingsTexts).Assembly);

    /// <summary>«Elegí un número entre {0} y {1}.», with the bounds in the numbers of the language of the request.</summary>
    public static string OutOfRange(int minimum, int maximum) =>
        DisplayNumbers.Format(CultureInfo.CurrentUICulture.Name, Texts.GetString(nameof(OutOfRange), CultureInfo.CurrentUICulture)
            ?? throw new InvalidOperationException("The forms settings text OutOfRange is missing."), minimum, maximum);
}
