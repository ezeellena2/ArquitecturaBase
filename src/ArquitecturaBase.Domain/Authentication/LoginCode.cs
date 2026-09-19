using System.Security.Cryptography;
using System.Text;
using ArquitecturaBase.Domain.Common;
using ArquitecturaBase.Domain.Results;
using ArquitecturaBase.Domain.ValueObjects;

namespace ArquitecturaBase.Domain.Authentication;

/// <summary>
/// Código de ingreso de un solo uso enviado por email (sección 5.3 del spec). Solo se guarda su hash.
/// Vence, admite una cantidad máxima de intentos y queda invalidado cuando se pide otro.
/// </summary>
public sealed class LoginCode : AggregateRoot
{
    // Para EF Core.
    private LoginCode()
    {
        Email = string.Empty;
        CodeHash = string.Empty;
    }

    private LoginCode(string email, string codeHash, DateTime createdAtUtc, DateTime expiresAtUtc, int maxAttempts)
    {
        Email = email;
        CodeHash = codeHash;
        CreatedAtUtc = createdAtUtc;
        ExpiresAtUtc = expiresAtUtc;
        MaxAttempts = maxAttempts;
    }

    public string Email { get; private set; }

    public string CodeHash { get; private set; }

    public DateTime CreatedAtUtc { get; private set; }

    public DateTime ExpiresAtUtc { get; private set; }

    public DateTime? ConsumedAtUtc { get; private set; }

    public DateTime? InvalidatedAtUtc { get; private set; }

    public int FailedAttempts { get; private set; }

    public int MaxAttempts { get; private set; }

    public int AttemptsLeft => Math.Max(0, MaxAttempts - FailedAttempts);

    public static LoginCode Issue(Email email, string codeHash, DateTime nowUtc, TimeSpan lifetime, int maxAttempts)
    {
        ArgumentNullException.ThrowIfNull(email);
        ArgumentException.ThrowIfNullOrWhiteSpace(codeHash);
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(lifetime, TimeSpan.Zero);
        ArgumentOutOfRangeException.ThrowIfLessThan(maxAttempts, 1);

        return new LoginCode(email.Value, codeHash, nowUtc, nowUtc + lifetime, maxAttempts);
    }

    public bool IsActive(DateTime nowUtc) =>
        ConsumedAtUtc is null && InvalidatedAtUtc is null && nowUtc < ExpiresAtUtc && FailedAttempts < MaxAttempts;

    /// <summary>Lo invalida porque se pidió un código nuevo. Conserva el primer momento de invalidación.</summary>
    public void Invalidate(DateTime nowUtc)
    {
        InvalidatedAtUtc ??= nowUtc;
    }

    public Result Verify(string codeHash, DateTime nowUtc)
    {
        ArgumentNullException.ThrowIfNull(codeHash);

        if (ConsumedAtUtc is not null)
        {
            return LoginCodeErrors.AlreadyUsed;
        }

        if (InvalidatedAtUtc is not null)
        {
            return LoginCodeErrors.Invalid(attemptsLeft: null);
        }

        if (nowUtc >= ExpiresAtUtc)
        {
            return LoginCodeErrors.Expired;
        }

        if (FailedAttempts >= MaxAttempts)
        {
            return LoginCodeErrors.TooManyAttempts;
        }

        // Comparación en tiempo constante: no revela cuántos caracteres coinciden.
        if (!CryptographicOperations.FixedTimeEquals(Encoding.UTF8.GetBytes(CodeHash), Encoding.UTF8.GetBytes(codeHash)))
        {
            FailedAttempts++;

            return FailedAttempts >= MaxAttempts ? LoginCodeErrors.TooManyAttempts : LoginCodeErrors.Invalid(AttemptsLeft);
        }

        ConsumedAtUtc = nowUtc;

        return Result.Success();
    }
}
