using ArquitecturaBase.Domain.Common;

namespace ArquitecturaBase.Domain.ValueObjects;

/// <summary>Qué es una cuenta para transferir: una CBU (de un banco), una CVU (de un proveedor de servicios de pago) o un alias.</summary>
public enum BankAccountKind { Cbu, Cvu, Alias }

/// <summary>
/// Una cuenta para transferir en la Argentina. Una CBU o una CVU tiene 22 dígitos en dos bloques, cada uno con su verificador
/// (dígitos 1 a 7 con los pesos 7, 1, 3, 9, 7, 1, 3 y el verificador en el 8; dígitos 9 a 21 con 3, 9, 7, 1, 3, 9, 7, 1, 3, 9, 7,
/// 1, 3 y el verificador en el 22; cada verificador es (10 - suma % 10) % 10), y una CVU empieza con 000. Un alias tiene de 6 a 20
/// letras, números, puntos o guiones. En una CBU o una CVU dice además el código de su entidad: los dígitos 1 a 3 de una CBU (el
/// banco) y los 4 a 7 de una CVU (el proveedor). Si un alias puede ser sólo números no se pudo confirmar con el BCRA, así que se
/// acepta, salvo con el largo de una CBU (22 dígitos, que son una CBU o una CVU y dan o no dan) o de una CUIT (11, que no es una
/// cuenta); una clave de puros ceros nunca es válida.
/// </summary>
public sealed class BankAccountKey : ValueObject
{
    public const int Digits = 22;
    public const int MinAliasLength = 6;
    public const int MaxAliasLength = 20;
    private const int CuitDigits = 11;

    private static readonly int[] FirstBlockWeights = [7, 1, 3, 9, 7, 1, 3];
    private static readonly int[] SecondBlockWeights = [3, 9, 7, 1, 3, 9, 7, 1, 3, 9, 7, 1, 3];

    private BankAccountKey(string value, BankAccountKind kind, string? entityCode)
    {
        Value = value;
        Kind = kind;
        EntityCode = entityCode;
    }

    /// <summary>La cuenta normalizada: los 22 dígitos de una CBU o una CVU, o el alias en minúsculas.</summary>
    public string Value { get; }

    public BankAccountKind Kind { get; }

    /// <summary>El código de la entidad: tres dígitos en una CBU, cuatro en una CVU y null en un alias.</summary>
    public string? EntityCode { get; }

    /// <summary>
    /// La cuenta normalizada, o null si no es una CBU, una CVU ni un alias válidos. Los dígitos pueden venir separados por cualquier
    /// espacio o guion, como en un comprobante o al copiarlos (<see cref="KeySeparators"/>); 22 u 11 dígitos nunca se leen como alias.
    /// </summary>
    public static BankAccountKey? Parse(string? value)
    {
        if (value is null) return null;
        var digits = KeySeparators.Strip(value);
        if (KeySeparators.IsAllZeros(digits)) return null;
        if (digits.Length is Digits or CuitDigits && digits.All(char.IsAsciiDigit))
        {
            if (digits.Length == CuitDigits
                || !Verifies(digits.AsSpan(0, 7), FirstBlockWeights, digits[7]) || !Verifies(digits.AsSpan(8, 13), SecondBlockWeights, digits[21]))
                return null;
            return digits.StartsWith("000", StringComparison.Ordinal)
                ? new BankAccountKey(digits, BankAccountKind.Cvu, digits[3..7])
                : new BankAccountKey(digits, BankAccountKind.Cbu, digits[..3]);
        }
        var alias = value.Trim();
        return alias.Length is >= MinAliasLength and <= MaxAliasLength && alias.All(c => char.IsAsciiLetterOrDigit(c) || c is '.' or '-')
            ? new BankAccountKey(alias.ToLowerInvariant(), BankAccountKind.Alias, null)
            : null;
    }

    /// <summary>La cuenta para un log o un texto: el principio y el final de los dígitos, o el principio del alias.</summary>
    public override string ToString() => Kind == BankAccountKind.Alias ? $"{Value[..2]}***" : $"{Value[..3]}***{Value[^4..]}";

    protected override IEnumerable<object?> GetEqualityComponents()
    {
        yield return Value;
    }

    private static bool Verifies(ReadOnlySpan<char> block, int[] weights, char check)
    {
        var sum = 0;
        for (var i = 0; i < block.Length; i++) sum += (block[i] - '0') * weights[i];
        return (10 - sum % 10) % 10 == check - '0';
    }
}
