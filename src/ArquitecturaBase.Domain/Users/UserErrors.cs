using ArquitecturaBase.Domain.Results;

namespace ArquitecturaBase.Domain.Users;

public static class UserErrors
{
    public const string EmailInvalidCode = "Users.Email.Invalid";
    public const string PhoneInvalidCode = "Users.Phone.Invalid";
    public const string NotFoundCode = "Users.User.NotFound";
    public const string CannotModifySelfCode = "Users.User.CannotModifySelf";
    public const string LastAdminCode = "Users.User.LastAdmin";
    public const string AlreadyExistsCode = "Users.User.AlreadyExists";

    public static readonly Error EmailInvalid = Error.Validation(EmailInvalidCode, "The email address is not valid.");

    public static readonly Error PhoneInvalid = Error.Validation(PhoneInvalidCode, "The phone number is not valid.");

    public static readonly Error NotFound = Error.NotFound(NotFoundCode, "The user was not found.");

    /// <summary>Desactivar o eliminar la propia cuenta, o quitarse a uno mismo el rol Admin.</summary>
    public static readonly Error CannotModifySelf = Error.Conflict(
        CannotModifySelfCode, "This change cannot be applied to your own account.");

    /// <summary>La acción dejaría al sistema sin ningún administrador activo.</summary>
    public static readonly Error LastAdmin = Error.Conflict(
        LastAdminCode, "The system must keep at least one active administrator.");

    /// <summary>Alta de un correo que ya tiene una cuenta activa. Una cuenta borrada no da este error: se restaura.</summary>
    public static readonly Error AlreadyExists = Error.Conflict(AlreadyExistsCode, "An account with that email already exists.");
}
