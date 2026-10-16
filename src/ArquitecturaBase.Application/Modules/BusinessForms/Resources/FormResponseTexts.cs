using System.Globalization;
using System.Resources;
namespace ArquitecturaBase.Application.Modules.BusinessForms.Resources;
internal static class FormResponseTexts
{
    private static readonly ResourceManager Texts = new("ArquitecturaBase.Application.Modules.BusinessForms.Resources.Responses", typeof(FormResponseTexts).Assembly);
    public static string Get(string key, string culture) => Texts.GetString(key, CultureInfo.GetCultureInfo(culture))
        ?? throw new InvalidOperationException("A form response label is missing.");
}
