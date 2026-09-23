using System.Globalization;
using System.Resources;

namespace ArquitecturaBase.Application.Resources;

/// <summary>
/// Los textos del bot de WhatsApp (Bot.resx), copiados del tablero "WhatsApp · Conversaciones con el bot". El idioma no
/// sale de la petición, porque el bot contesta desde segundo plano: lo pasa quien arma el mensaje (el de la cuenta, o
/// español si no hay cuenta).
/// </summary>
public static class BotTexts
{
    internal static ResourceManager ResourceManager { get; } =
        new("ArquitecturaBase.Application.Resources.Bot", typeof(BotTexts).Assembly);

    /// <summary>El texto de <paramref name="key"/> en <paramref name="culture"/>, con los valores en su lugar.</summary>
    public static string Get(string key, CultureInfo culture, params object[] args)
    {
        ArgumentNullException.ThrowIfNull(culture);

        // Una clave que falta es un error de programación: ResourceParityTests y los tests del bot la encuentran antes.
        var text = ResourceManager.GetString(key, culture)
            ?? throw new InvalidOperationException($"Missing bot text '{key}'.");

        return args.Length == 0 ? text : string.Format(culture, text, args);
    }
}
