using System.Globalization;
using System.Resources;

namespace ArquitecturaBase.Application.Modules.ArtificialIntelligence.Resources;

internal static class AssistantTexts
{
    internal static ResourceManager Manager { get; } = new("ArquitecturaBase.Application.Modules.ArtificialIntelligence.Resources.Assistant", typeof(AssistantTexts).Assembly);
    // A failure code without its own text is shown as the provider being unavailable.
    public static string Get(string key) => Manager.GetString(key, CultureInfo.CurrentUICulture) ?? Manager.GetString("Ai.Assistant.ProviderUnavailable", CultureInfo.CurrentUICulture)!;
    public static string Get(string key, string culture) => Manager.GetString(key, CultureInfo.GetCultureInfo(culture)) ?? Get(key);

    // An instruction for the model has no fallback: a missing key would send the text of a failure as instructions (B4).
    public static string Prompt(string key, string culture) => Manager.GetString(key, CultureInfo.GetCultureInfo(culture))
        ?? throw new InvalidOperationException($"The assistant instruction '{key}' is missing.");
}
