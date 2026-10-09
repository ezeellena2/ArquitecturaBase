using System.Globalization;
using System.Resources;

namespace ArquitecturaBase.Application.Modules.WhatsApp.Resources;

public static class AccessTexts
{
    internal static ResourceManager ResourceManager { get; } =
        new("ArquitecturaBase.Application.Modules.WhatsApp.Resources.Access", typeof(AccessTexts).Assembly);

    public static string Get(string key, CultureInfo culture)
    {
        ArgumentNullException.ThrowIfNull(culture);
        return ResourceManager.GetString(key, culture)
            ?? throw new InvalidOperationException($"Missing access text '{key}'.");
    }
}
