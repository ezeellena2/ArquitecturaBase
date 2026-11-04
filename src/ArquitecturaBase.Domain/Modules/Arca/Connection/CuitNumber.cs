namespace ArquitecturaBase.Domain.Modules.Arca.Connection;

/// <summary>La CUIT o CUIL de ARCA: once dígitos con el verificador de módulo 11.</summary>
public static class CuitNumber
{
    private static readonly int[] Weights = [5, 4, 3, 2, 7, 6, 5, 4, 3, 2];

    /// <summary>
    /// Los once dígitos de una CUIT válida, sin guiones ni espacios, o null. Un verificador 10 nunca es válido: ARCA no lo
    /// asigna, cambia el prefijo.
    /// </summary>
    public static string? Normalize(string? value)
    {
        if (value is null) return null;
        var digits = new string(value.Where(c => c is not ('-' or ' ')).ToArray());
        if (digits.Length != 11 || !digits.All(char.IsAsciiDigit)) return null;
        var sum = 0;
        for (var i = 0; i < 10; i++) sum += (digits[i] - '0') * Weights[i];
        var check = 11 - sum % 11;
        if (check == 11) check = 0;
        return check != 10 && digits[10] - '0' == check ? digits : null;
    }

    /// <summary>La CUIT para un log o un texto: el prefijo y los últimos dígitos, como los teléfonos.</summary>
    public static string Mask(string cuit) => cuit.Length == 11 ? $"{cuit[..2]}-*****{cuit[7..10]}-{cuit[10]}" : "***";
}
