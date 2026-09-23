using ArquitecturaBase.Domain.Results;

namespace ArquitecturaBase.Domain.Authentication;

public static class AccountErrors
{
    public const string DisabledCode = "Auth.Account.Disabled";
    public const string LockedOutCode = "Auth.Account.LockedOut";
    public const string NotInvitedCode = "Auth.Account.NotInvited";

    /// <summary>Cuenta deshabilitada. Se informa después de verificar el código: el usuario ya probó que el email es suyo.</summary>
    public static readonly Error Disabled = Error.Forbidden(DisabledCode, "The account is disabled.");

    /// <summary>Bloqueo de Identity por verificaciones fallidas seguidas.</summary>
    public static readonly Error LockedOut = Error.TooManyRequests(LockedOutCode, "The account is temporarily locked.");

    /// <summary>
    /// El sistema es solo por invitación y ese email no tiene cuenta. Solo se informa cuando la persona ya probó que la
    /// dirección es suya, ante el proveedor externo o con el código: por eso no revela nada (sección 4 del spec de la
    /// Fase 4).
    /// </summary>
    public static readonly Error NotInvited = Error.Forbidden(
        NotInvitedCode, "The account must be created by an administrator.");
}
