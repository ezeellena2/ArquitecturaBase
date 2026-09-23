using System.Text;

namespace ArquitecturaBase.Domain.WhatsApp;

/// <summary>
/// Los textos que llegan de WhatsApp, listos para guardar. Postgres no guarda el carácter nulo (U+0000) en un texto, y
/// Npgsql no puede mandar la mitad suelta de un par sustituto (medio emoji): cualquiera de los dos hace fallar el
/// guardado del webhook entero, y Meta lo reintentaría igual durante días. Por eso el texto que elige la persona se
/// limpia en lugar de rechazarse.
/// </summary>
internal static class WhatsAppText
{
    /// <summary>Si se puede guardar tal cual: sin carácter nulo y sin mitades sueltas de un par sustituto.</summary>
    public static bool IsStorable(ReadOnlySpan<char> text)
    {
        for (var i = 0; i < text.Length; i++)
        {
            var current = text[i];

            if (current == '\0' || char.IsLowSurrogate(current))
            {
                return false;
            }

            if (char.IsHighSurrogate(current))
            {
                if (i + 1 == text.Length || !char.IsLowSurrogate(text[i + 1]))
                {
                    return false;
                }

                i++;
            }
        }

        return true;
    }

    /// <summary>
    /// El texto limpio y recortado a <paramref name="maxLength"/>: sin el carácter nulo, con el carácter de reemplazo
    /// (U+FFFD) en lugar de cada mitad suelta, y sin partir un emoji en el corte. Null si no queda nada.
    /// </summary>
    public static string? Clean(string? text, int maxLength)
    {
        if (string.IsNullOrEmpty(text))
        {
            return null;
        }

        var cleaned = Truncate(Sanitize(text), maxLength);

        return cleaned.Length == 0 ? null : cleaned;
    }

    /// <summary>Sin el carácter nulo y con U+FFFD en lugar de cada mitad suelta de un par sustituto.</summary>
    public static string Sanitize(string text)
    {
        if (IsStorable(text))
        {
            return text;
        }

        var builder = new StringBuilder(text.Length);
        Span<char> units = stackalloc char[2];

        // EnumerateRunes ya entrega cada mitad suelta como U+FFFD.
        foreach (var rune in text.EnumerateRunes())
        {
            if (rune.Value != 0)
            {
                builder.Append(units[..rune.EncodeToUtf16(units)]);
            }
        }

        return builder.ToString();
    }

    /// <summary>
    /// Recorta a <paramref name="maxLength"/> unidades de UTF-16 un texto ya limpio (<see cref="Sanitize"/>). Si el
    /// corte cae entre las dos mitades de un emoji, el emoji queda afuera entero.
    /// </summary>
    public static string Truncate(string text, int maxLength)
    {
        if (text.Length <= maxLength)
        {
            return text;
        }

        return text[..(char.IsHighSurrogate(text[maxLength - 1]) ? maxLength - 1 : maxLength)];
    }
}
