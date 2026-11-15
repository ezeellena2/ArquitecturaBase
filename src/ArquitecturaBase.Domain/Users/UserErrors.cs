using ArquitecturaBase.Domain.Results;

namespace ArquitecturaBase.Domain.Users;

public static class UserErrors
{
    public const string EmailInvalidCode = "Users.Email.Invalid";
    public const string PhoneInvalidCode = "Users.Phone.Invalid";
    public const string PhoneAlreadyExistsCode = "Users.Phone.AlreadyExists";
    public const string NotFoundCode = "Users.User.NotFound";
    public const string CannotModifySelfCode = "Users.User.CannotModifySelf";
    public const string LastAdminCode = "Users.User.LastAdmin";
    public const string AlreadyExistsCode = "Users.User.AlreadyExists";
    public const string LastLoginMethodCode = "Users.User.LastLoginMethod";
    public const string IdentityRequiredCode = "Users.Identity.Required";

    public static readonly Error EmailInvalid = Error.Validation(EmailInvalidCode, "The email address is not valid.");

    public static readonly Error PhoneInvalid = Error.Validation(PhoneInvalidCode, "The phone number is not valid.");

    /// <summary>
    /// Toda cuenta tiene al menos un correo o un número (sección 6.1 del spec del ingreso con WhatsApp): el alta de un
    /// administrador necesita uno de los dos.
    /// </summary>
    public static readonly Error IdentityRequired = Error.Validation(IdentityRequiredCode, "An account needs an email or a phone number.");

    /// <summary>
    /// El número ya es de otra cuenta, activa o borrada: la cuenta borrada lo conserva (sección 6.1 del spec del
    /// ingreso con WhatsApp). Desde el perfil se dice recién después de un código correcto, que solo tiene quien es
    /// dueño del número: si se dijera antes, cualquiera averiguaría qué números están registrados.
    /// </summary>
    public static readonly Error PhoneAlreadyExists = Error.Conflict(PhoneAlreadyExistsCode, "An account with that phone number already exists.");

    public static readonly Error NotFound = Error.NotFound(NotFoundCode, "The user was not found.");

    /// <summary>Desactivar o eliminar la propia cuenta, o quitarse a uno mismo el rol Admin.</summary>
    public static readonly Error CannotModifySelf = Error.Conflict(
        CannotModifySelfCode, "This change cannot be applied to your own account.");

    /// <summary>La acción dejaría al sistema sin ningún administrador activo.</summary>
    public static readonly Error LastAdmin = Error.Conflict(
        LastAdminCode, "The system must keep at least one active administrator.");

    /// <summary>
    /// Alta de un correo que ya tiene una cuenta activa; en el alta, una cuenta borrada no da este error: se restaura.
    /// Desde el perfil, en cambio, el correo de otra cuenta, activa o borrada, no se puede agregar, y se dice recién
    /// después de un código correcto, como con el número.
    /// </summary>
    public static readonly Error AlreadyExists = Error.Conflict(AlreadyExistsCode, "An account with that email already exists.");

    /// <summary>
    /// Desvincular el propio WhatsApp dejaría a la cuenta sin cómo entrar: no tiene un correo verificado ni un Google
    /// vinculado (sección 12 del spec del ingreso con WhatsApp).
    /// </summary>
    public static readonly Error LastLoginMethod = Error.Conflict(
        LastLoginMethodCode, "The account has no other way to sign in.");
}
