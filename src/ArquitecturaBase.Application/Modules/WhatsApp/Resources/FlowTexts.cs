using System.Globalization;
using System.Resources;

namespace ArquitecturaBase.Application.Modules.WhatsApp.Resources;

internal static class FlowTexts
{
    internal static ResourceManager ResourceManager { get; } = new("ArquitecturaBase.Application.Modules.WhatsApp.Resources.FlowTexts", typeof(FlowTexts).Assembly);
    public static string Get(string key, string culture) => ResourceManager.GetString(key, CultureInfo.GetCultureInfo(culture))
        ?? throw new InvalidOperationException("The flow resource is missing.");
}
