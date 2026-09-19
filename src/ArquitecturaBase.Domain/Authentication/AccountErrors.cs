using ArquitecturaBase.Domain.Results;

namespace ArquitecturaBase.Domain.Authentication;

public static class AccountErrors
{
    public const string DisabledCode = "Auth.Account.Disabled";
    public const string LockedOutCode = "Auth.Account.LockedOut";

    /// <summary>Cuenta deshabilitada. Se informa después de verificar el código: el usuario ya probó que el email es suyo.</summary>
    public static readonly Error Disabled = Error.Forbidden(DisabledCode, "The account is disabled.");

    /// <summary>Bloqueo de Identity por verificaciones fallidas seguidas.</summary>
    public static readonly Error LockedOut = Error.TooManyRequests(LockedOutCode, "The account is temporarily locked.");
}
