namespace ArquitecturaBase.Infrastructure.Persistence.Extensions;

/// <summary>
/// Patrones de LIKE/ILIKE armados con texto que escribe una persona. "%" y "_" del texto buscado son literales, no
/// comodines: se escapan con <see cref="EscapeCharacter"/>. Quien usa un patrón de acá SIEMPRE le pasa
/// <see cref="EscapeCharacter"/> al Like/ILike (<c>EF.Functions.ILike(columna, patrón, LikePatterns.EscapeCharacter)</c>);
/// sin él, la barra no escapa nada y "a_b" vuelve a matchear "axb".
/// </summary>
internal static class LikePatterns
{
    public const string EscapeCharacter = "\\";

    /// <summary>"Contiene": <c>%texto%</c>, con el texto escapado. No recorta ni cambia mayúsculas.</summary>
    public static string Contains(string value)
    {
        ArgumentNullException.ThrowIfNull(value);

        return "%" + Escape(value) + "%";
    }

    // Primero la barra: si no, se volverían a escapar las barras que agregan los reemplazos de "%" y "_".
    private static string Escape(string value) =>
        value
            .Replace(EscapeCharacter, EscapeCharacter + EscapeCharacter, StringComparison.Ordinal)
            .Replace("%", EscapeCharacter + "%", StringComparison.Ordinal)
            .Replace("_", EscapeCharacter + "_", StringComparison.Ordinal);
}
