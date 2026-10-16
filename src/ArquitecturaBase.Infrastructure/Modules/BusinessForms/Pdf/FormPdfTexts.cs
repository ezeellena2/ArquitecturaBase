using System.Globalization;
using System.Resources;
using ArquitecturaBase.Application.Modules.BusinessForms.Models;

namespace ArquitecturaBase.Infrastructure.Modules.BusinessForms.Pdf;

internal static class FormPdfTexts
{
    private static readonly ResourceManager Resources = new("ArquitecturaBase.Application.Modules.BusinessForms.Resources.Pdf", typeof(FormPdfDesign).Assembly);
    public static string Get(string key, string culture) => Resources.GetString(key, CultureInfo.GetCultureInfo(culture))
        ?? throw new InvalidOperationException("A PDF resource is missing.");
    public static string Format(string key, string culture, params object[] values) => string.Format(CultureInfo.GetCultureInfo(culture), Get(key, culture), values);
}
