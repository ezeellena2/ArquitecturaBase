namespace ArquitecturaBase.Domain.ValueObjects;

/// <summary>
/// La CUIT o el CUIL: once dígitos con el verificador de módulo 11 (pesos 5, 4, 3, 2, 7, 6, 5, 4, 3, 2; un resto que deja 11 es
/// 0 y uno que deja 10 nunca es válido). Es del núcleo: la usa todo lo que valida una CUIT o un CUIL.
/// </summary>
public static class CuitNumber
{
    private static readonly int[] Weights = [5, 4, 3, 2, 7, 6, 5, 4, 3, 2];

    /// <summary>
    /// Los once dígitos de una CUIT o un CUIL válidos, sin separadores (cualquier espacio o guion, <see cref="KeySeparators"/>), o
    /// null. Un verificador 10 nunca es válido: no se asigna, se cambia el prefijo. Once ceros suman bien, pero no son una CUIT.
    /// </summary>
    public static string? Normalize(string? value)
    {
        if (value is null) return null;
        var digits = KeySeparators.Strip(value);
        if (digits.Length != 11 || !digits.All(char.IsAsciiDigit) || KeySeparators.IsAllZeros(digits)) return null;
        var sum = 0;
        for (var i = 0; i < 10; i++) sum += (digits[i] - '0') * Weights[i];
        var check = 11 - sum % 11;
        if (check == 11) check = 0;
        return check != 10 && digits[10] - '0' == check ? digits : null;
    }

    /// <summary>
    /// Si el texto tiene la forma de una CUIT o un CUIL, dé o no su verificador: once dígitos con cualquier separador. Un DNI o un
    /// texto no la tienen, y nadie podría corregirlos como CUIT copiándolos de un comprobante (O44).
    /// </summary>
    public static bool HasShape(string? value) =>
        value is not null && KeySeparators.Strip(value) is { Length: 11 } digits && digits.All(char.IsAsciiDigit);

    /// <summary>La CUIT para un log o un texto: el prefijo y los últimos dígitos, como los teléfonos.</summary>
    public static string Mask(string cuit) => cuit.Length == 11 ? $"{cuit[..2]}-*****{cuit[7..10]}-{cuit[10]}" : "***";
}
