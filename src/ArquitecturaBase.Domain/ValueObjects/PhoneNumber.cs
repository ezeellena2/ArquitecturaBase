using ArquitecturaBase.Domain.Common;
using ArquitecturaBase.Domain.Results;
using ArquitecturaBase.Domain.Users;

namespace ArquitecturaBase.Domain.ValueObjects;

/// <summary>
/// Número de teléfono en formato internacional: "+" y de 8 a 15 dígitos, sin espacios ni separadores
/// (por ejemplo, +5491123456789). Interpretar lo que escribe una persona no se hace acá: lo hace
/// <c>IPhoneNumberParser</c>, que devuelve el número ya en este formato.
/// </summary>
public sealed class PhoneNumber : ValueObject
{
    public const int MaxLength = 16;

    private const int MinDigits = 8;
    private const int MaxDigits = MaxLength - 1;

    private PhoneNumber(string value)
    {
        Value = value;
    }

    public string Value { get; }

    public static Result<PhoneNumber> Create(string? value)
    {
        var trimmed = value?.Trim();

        if (string.IsNullOrEmpty(trimmed) || !HasValidFormat(trimmed))
        {
            return UserErrors.PhoneInvalid;
        }

        return new PhoneNumber(trimmed);
    }

    public override string ToString() => Value;

    protected override IEnumerable<object?> GetEqualityComponents()
    {
        yield return Value;
    }

    private static bool HasValidFormat(string value)
    {
        var digits = value.AsSpan(1);

        // Ningún código de país empieza con 0. Solo dígitos ASCII: char.IsDigit también acepta otros alfabetos.
        return value[0] == '+'
            && digits.Length is >= MinDigits and <= MaxDigits
            && digits[0] != '0'
            && !digits.ContainsAnyExceptInRange('0', '9');
    }
}
