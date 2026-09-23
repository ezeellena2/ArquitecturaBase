using ArquitecturaBase.Domain.Common;
using ArquitecturaBase.Domain.Results;

namespace ArquitecturaBase.Domain.Authentication;

/// <summary>
/// Enlace de ingreso de un solo uso, el que manda el bot de WhatsApp al chat (sección 6.4 del spec del ingreso con
/// WhatsApp). Solo se guarda el hash del token, que viaja en el fragmento de la URL. Vence a los 10 minutos, sirve una
/// sola vez y queda invalidado cuando se emite otro para la misma cuenta. Vencido, usado o invalidado, responde lo
/// mismo que un enlace que nunca existió: <see cref="LoginLinkErrors.Invalid"/>.
/// </summary>
public sealed class LoginLink : AggregateRoot
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

    /// <summary>La cuenta con la que entra quien canjea el enlace.</summary>
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

    /// <summary>Lo invalida porque se emitió otro para la misma cuenta. Conserva el primer momento de invalidación.</summary>
    public void Invalidate(DateTime nowUtc)
    {
        InvalidatedAtUtc ??= nowUtc;
    }

    /// <summary>
    /// Lo consume, si todavía sirve. Uno que ya no sirve no cambia, y el error es el mismo sea cual sea el motivo.
    /// </summary>
    public Result Redeem(DateTime nowUtc)
    {
        if (!IsActive(nowUtc))
        {
            return LoginLinkErrors.Invalid;
        }

        ConsumedAtUtc = nowUtc;

        return Result.Success();
    }
}
