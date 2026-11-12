using System.Globalization;

namespace ArquitecturaBase.Domain.ValueObjects;

/// <summary>
/// Lo que separa los dígitos de una clave copiada de un comprobante (una CBU, una CVU o una CUIT): cualquier espacio (también el que
/// no corta, U+00A0, el angosto, U+202F, o un tabulado) y cualquier guion (el corto, el medio, la raya y el signo menos, U+2212).
/// </summary>
internal static class KeySeparators
{
    public static bool Is(char value) => char.IsWhiteSpace(value) || value == '−'
        || CharUnicodeInfo.GetUnicodeCategory(value) == UnicodeCategory.DashPunctuation;

    /// <summary>La clave sin separadores.</summary>
    public static string Strip(string value) => string.Concat(value.Where(character => !Is(character)));

    /// <summary>Una clave de puros ceros: suma bien, pero nunca se asigna.</summary>
    public static bool IsAllZeros(string digits) => digits.Length > 0 && digits.All(character => character == '0');
}
