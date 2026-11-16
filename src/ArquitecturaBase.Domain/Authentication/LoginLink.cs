using ArquitecturaBase.Domain.Common;

namespace ArquitecturaBase.Domain.Authentication;

/// <summary>
/// Enlace de ingreso de un solo uso, el que mandaba el bot de WhatsApp al chat (sección 6.4 del spec del ingreso con
/// WhatsApp). Se retiró el 2026-11-16 (B4): nada lo emite ni lo canjea, y la tabla queda hasta que la vacíe la
/// retención. De cada uno se guardó solo el hash del token. Los pendientes se siguen invalidando cuando la cuenta suelta
/// su número o pierde el acceso.
/// </summary>
public sealed class LoginLink : Entity
{
    /// <summary>Cuánto dura un enlace: lo mismo que un código (sección 4 del spec del ingreso con WhatsApp).</summary>
    public static readonly TimeSpan Lifetime = TimeSpan.FromMinutes(10);

    // Para EF Core.
    private LoginLink()
    {
        TokenHash = string.Empty;
    }

    private LoginLink(Guid userId, string tokenHash, DateTime createdAtUtc)
    {
        UserId = userId;
        TokenHash = tokenHash;
        CreatedAtUtc = createdAtUtc;
        ExpiresAtUtc = createdAtUtc + Lifetime;
    }

    /// <summary>La cuenta con la que entraba quien canjeaba el enlace.</summary>
    public Guid UserId { get; private set; }

    /// <summary>El hash del token. El token en claro no se guarda en ningún lado.</summary>
    public string TokenHash { get; private set; }

    public DateTime CreatedAtUtc { get; private set; }

    public DateTime ExpiresAtUtc { get; private set; }

    public DateTime? ConsumedAtUtc { get; private set; }

    public DateTime? InvalidatedAtUtc { get; private set; }

    public static LoginLink Issue(Guid userId, string tokenHash, DateTime nowUtc)
    {
        if (userId == Guid.Empty)
        {
            throw new ArgumentException("A login link needs the account it signs in to.", nameof(userId));
        }

        ArgumentException.ThrowIfNullOrWhiteSpace(tokenHash);

        return new LoginLink(userId, tokenHash, nowUtc);
    }

    public bool IsActive(DateTime nowUtc) => ConsumedAtUtc is null && InvalidatedAtUtc is null && nowUtc < ExpiresAtUtc;

    /// <summary>Lo invalida porque la cuenta soltó su número o perdió el acceso. Conserva el primer momento.</summary>
    public void Invalidate(DateTime nowUtc)
    {
        InvalidatedAtUtc ??= nowUtc;
    }
}
