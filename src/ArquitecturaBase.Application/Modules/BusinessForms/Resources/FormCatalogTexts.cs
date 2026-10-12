using System.Globalization;
using System.Resources;

namespace ArquitecturaBase.Application.Modules.BusinessForms.Resources;

internal static class FormCatalogTexts
{
    private static readonly ResourceManager Texts = new(
        "ArquitecturaBase.Application.Modules.BusinessForms.Resources.Catalogs",
        typeof(FormCatalogTexts).Assembly);

    public static string Active => Get("Active");

    public static string Inactive => Get("Inactive");

    public static string ActiveValue(string value) => value switch
    {
        "true" => Active,
        "false" => Inactive,
        _ => throw new InvalidOperationException("The active catalog view returned an invalid boolean value."),
    };

    private static string Get(string key) => Texts.GetString(key, CultureInfo.CurrentUICulture)
        ?? throw new InvalidOperationException("A form catalog label is missing.");
}
