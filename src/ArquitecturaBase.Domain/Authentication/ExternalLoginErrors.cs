using ArquitecturaBase.Domain.Results;

namespace ArquitecturaBase.Domain.Authentication;

public static class ExternalLoginErrors
{
    public const string FailedCode = "Auth.ExternalLogin.Failed";
    public const string EmailNotVerifiedCode = "Auth.ExternalLogin.EmailNotVerified";
    public const string AccountEmailNotConfirmedCode = "Auth.ExternalLogin.AccountEmailNotConfirmed";

    public static readonly Error Failed = Error.Unauthorized(FailedCode, "The external sign-in could not be completed.");

    public static readonly Error EmailNotVerified = Error.Forbidden(
        EmailNotVerifiedCode, "The external provider did not verify the email address.");

    /// <summary>
    /// La cuenta de ese correo todavía no lo confirmó (lo cargó un administrador o volvió al restaurarla), así que el
    /// proveedor no se vincula por el correo: la dirección pudo pasar a otra persona y el dueño anterior conservar su cuenta
    /// del proveedor. Se entra primero con el código al correo (N4, 2026-11-15).
    /// </summary>
    public static readonly Error AccountEmailNotConfirmed = Error.Forbidden(
        AccountEmailNotConfirmedCode, "The account has not confirmed its email address yet. Sign in with an email code first.");
}
