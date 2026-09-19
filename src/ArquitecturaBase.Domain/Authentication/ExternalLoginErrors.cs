using ArquitecturaBase.Domain.Results;

namespace ArquitecturaBase.Domain.Authentication;

public static class ExternalLoginErrors
{
    public const string FailedCode = "Auth.ExternalLogin.Failed";
    public const string EmailNotVerifiedCode = "Auth.ExternalLogin.EmailNotVerified";

    public static readonly Error Failed = Error.Unauthorized(FailedCode, "The external sign-in could not be completed.");

    public static readonly Error EmailNotVerified = Error.Forbidden(
        EmailNotVerifiedCode, "The external provider did not verify the email address.");
}
