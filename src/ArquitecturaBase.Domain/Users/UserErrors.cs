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
    public const string RolesNotHeldCode = "Users.User.RolesNotHeld";
    public const string HasMorePermissionsCode = "Users.User.HasMorePermissions";
    public const string InitialAdminCode = "Users.User.InitialAdmin";
    public const string EmailAlreadySetCode = "Users.User.EmailAlreadySet";
    public const string PhoneCodeUnavailableCode = "Users.User.PhoneCodeUnavailable";
    public const string ConfirmationCodeRequiredCode = "Users.User.ConfirmationCodeRequired";
    public const string NoVerifiedEmailCode = "Users.User.NoVerifiedEmail";
    public const string PhoneAlreadySetCode = "Users.User.PhoneAlreadySet";
    public const string NoPhoneCode = "Users.User.NoPhone";
    public const string NoVerifiedPhoneCode = "Users.User.NoVerifiedPhone";

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

    /// <summary>
    /// Dar o quitar un rol con permisos que quien pide no tiene, en el alta o en la edición. Darle a una cuenta el correo
    /// de <c>Seed:AdminEmail</c> cuenta como darle Admin: el arranque se lo da.
    /// </summary>
    public static readonly Error RolesNotHeld = Error.Forbidden(
        RolesNotHeldCode, "You can't give or take away a role with permissions you don't have.");

    /// <summary>
    /// Cambiarle el correo o el número, desvincularle el número, desactivar, reactivar o borrar una cuenta con permisos
    /// que quien pide no tiene.
    /// </summary>
    public static readonly Error HasMorePermissions = Error.Forbidden(
        HasMorePermissionsCode, "The account has permissions you don't have.");

    /// <summary>
    /// Quitarle Admin o cambiarle el correo a la cuenta de <c>Seed:AdminEmail</c>, desde la administración o desde su
    /// propio perfil: el arranque la busca por ese correo y le devuelve Admin, así que primero hay que cambiar esa clave
    /// de la configuración.
    /// </summary>
    public static readonly Error InitialAdmin = Error.Conflict(
        InitialAdminCode, "This is the initial administrator's account: Seed:AdminEmail has to change in the configuration first.");

    /// <summary>
    /// El perfil agrega un correo, pero no cambia uno verificado (N11): con una sesión abierta ajena, cambiarlo sería
    /// quedarse con la cuenta. Lo cambia un administrador.
    /// </summary>
    public static readonly Error EmailAlreadySet = Error.Conflict(
        EmailAlreadySetCode, "The account already has a verified email, and it can't be changed from the profile.");

    /// <summary>
    /// Agregar un correo a una cuenta con número verificado pide un código a ese número (N11), y ahora el canal
    /// telefónico no manda códigos (sin WhatsApp, o apagado).
    /// </summary>
    public static readonly Error PhoneCodeUnavailable = Error.Conflict(
        PhoneCodeUnavailableCode, "The change needs a code sent to the account's phone number, and no codes can be sent to phones now.");

    /// <summary>
    /// El cambio pide el código que se mandó al medio que la cuenta ya tiene (N11: el del número para agregar un
    /// correo, el del correo para desvincular el número), y no vino. Va atado al campo de ese código.
    /// </summary>
    public static readonly Error ConfirmationCodeRequired = Error.Validation(
        ConfirmationCodeRequiredCode, "The change needs the code sent to the account's other sign-in method.");

    /// <summary>
    /// Se pidió al correo el código que confirma un cambio del número, y la cuenta no tiene un correo verificado.
    /// </summary>
    public static readonly Error NoVerifiedEmail = Error.Conflict(
        NoVerifiedEmailCode, "The account has no verified email to send the code to.");

    /// <summary>
    /// El perfil vincula un número, pero no reemplaza uno verificado (cada medio confirma los cambios del otro,
    /// 2026-11-16): para cambiarlo, se desvincula (con el código del correo) y se vincula el nuevo; en una cuenta sólo con
    /// WhatsApp lo cambia un administrador. Uno sin verificar, que cargó un administrador, sí se reemplaza.
    /// </summary>
    public static readonly Error PhoneAlreadySet = Error.Conflict(
        PhoneAlreadySetCode, "The account already has a verified phone number, and it can't be replaced from the profile.");

    /// <summary>Se pidió el código que confirma desvincular el número, y la cuenta no tiene número.</summary>
    public static readonly Error NoPhone = Error.Conflict(NoPhoneCode, "The account has no phone number to unlink.");

    /// <summary>
    /// Se pidió al número el código que confirma agregar un correo, y la cuenta no tiene un número verificado.
    /// </summary>
    public static readonly Error NoVerifiedPhone = Error.Conflict(
        NoVerifiedPhoneCode, "The account has no verified phone number to send the code to.");
}
