using System.Globalization;
using System.Resources;

namespace ArquitecturaBase.Application.Resources;

internal static class SystemHelpTexts
{
    private static readonly ResourceManager Manager = new("ArquitecturaBase.Application.Resources.SystemHelp", typeof(SystemHelpTexts).Assembly);
    public static string Get(string key, string culture) => Manager.GetString(key, CultureInfo.GetCultureInfo(culture))
        ?? throw new InvalidOperationException($"The system help resource '{key}' is missing.");
}
