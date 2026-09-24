using ArquitecturaBase.Domain.Authentication;
using ArquitecturaBase.Domain.Results;

namespace ArquitecturaBase.Domain.Users;

public static class UserInvitationErrors
{
    public const string ConsentRequiredCode = "Users.Invitation.ConsentRequired";
    public const string NameRequiredCode = "Users.Invitation.NameRequired";
    public const string TooManyRequestsCode = "Users.Invitation.TooManyRequests";
    public const string UserInactiveCode = "Users.Invitation.UserInactive";

    /// <summary>La misma clave que los límites de los códigos y de los enlaces: el front lee siempre "retryAfter".</summary>
    public const string RetryAfterKey = LoginCodeErrors.RetryAfterKey;

    /// <summary>
    /// Para escribirle primero por WhatsApp, la persona tiene que haber aceptado recibir mensajes: el admin lo confirma al
    /// invitar (sección 6.6 del spec del ingreso con WhatsApp).
    /// </summary>
    public static readonly Error ConsentRequired = Error.Validation(
        ConsentRequiredCode, "Confirm that the person accepted to receive WhatsApp messages.");

    /// <summary>La plantilla saluda por el nombre, y Meta no acepta un parámetro vacío (decisión 1 del plan).</summary>
    public static readonly Error NameRequired = Error.Validation(
        NameRequiredCode, "Inviting by WhatsApp needs the name of the person.");

    /// <summary>
    /// Una cuenta desactivada no se invita: no podría entrar con la invitación. Una borrada no se encuentra
    /// (<see cref="UserErrors.NotFound"/>), como en el resto de la administración.
    /// </summary>
    public static readonly Error UserInactive = Error.Validation(UserInactiveCode, "A deactivated account cannot be invited.");

    /// <summary>
    /// Pasó menos de un minuto desde la última invitación a la misma cuenta (<see cref="UserInvitation.ResendCooldown"/>).
    /// </summary>
    public static Error TooManyRequests(int retryAfterSeconds) =>
        Error.TooManyRequests(
            TooManyRequestsCode,
            "The account was invited less than a minute ago.",
            new Dictionary<string, object?> { [RetryAfterKey] = retryAfterSeconds });
}
