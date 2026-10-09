using System.Globalization;
using System.Resources;

namespace ArquitecturaBase.Application.Modules.ArtificialIntelligence.Resources;

internal static class AssistantTexts
{
    internal static ResourceManager Manager { get; } = new("ArquitecturaBase.Application.Modules.ArtificialIntelligence.Resources.Assistant", typeof(AssistantTexts).Assembly);
    public static string Get(string key) => Manager.GetString(key, CultureInfo.CurrentUICulture) ?? Manager.GetString("Ai.Assistant.ProviderUnavailable", CultureInfo.CurrentUICulture)!;
    public static string Get(string key, string culture) => Manager.GetString(key, CultureInfo.GetCultureInfo(culture)) ?? Get(key);
}
