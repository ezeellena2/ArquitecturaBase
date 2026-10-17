using System.Globalization;
using System.Resources;

namespace ArquitecturaBase.Application.Modules.WhatsApp.Resources;

internal static class AssistantTerminalTexts
{
    internal static ResourceManager ResourceManager { get; } = new("ArquitecturaBase.Application.Modules.WhatsApp.Resources.AssistantTerminalTexts", typeof(AssistantTerminalTexts).Assembly);
    public static string Get(string key, string culture) => ResourceManager.GetString(key, CultureInfo.GetCultureInfo(culture))
        ?? throw new InvalidOperationException("The assistant terminal resource is missing.");
}
