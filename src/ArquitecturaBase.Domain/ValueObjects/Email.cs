using ArquitecturaBase.Domain.Common;
using ArquitecturaBase.Domain.Results;
using ArquitecturaBase.Domain.Users;

namespace ArquitecturaBase.Domain.ValueObjects;

/// <summary>Email normalizado (sin espacios alrededor y en minúsculas) con un formato básico validado.</summary>
public sealed class Email : ValueObject
{
    public const int MaxLength = 254;

    private Email(string value)
    {
        Value = value;
    }

    public string Value { get; }

    public static Result<Email> Create(string? value)
    {
        var normalized = value?.Trim().ToLowerInvariant();

        if (string.IsNullOrEmpty(normalized) || normalized.Length > MaxLength || !HasValidFormat(normalized))
        {
            return UserErrors.EmailInvalid;
        }

        return new Email(normalized);
    }

    public override string ToString() => Value;

    protected override IEnumerable<object?> GetEqualityComponents()
    {
        yield return Value;
    }

    private static bool HasValidFormat(string value)
    {
        var at = value.IndexOf('@', StringComparison.Ordinal);

        return at > 0
            && at == value.LastIndexOf('@')
            && at < value.Length - 1
            && !value.Any(char.IsWhiteSpace)
            && value[(at + 1)..].Contains('.', StringComparison.Ordinal)
            && !value.EndsWith('.');
    }
}
