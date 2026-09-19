using ArquitecturaBase.Domain.Results;

namespace ArquitecturaBase.Domain.Users;

public static class UserErrors
{
    public const string EmailInvalidCode = "Users.Email.Invalid";
    public const string NotFoundCode = "Users.User.NotFound";

    public static readonly Error EmailInvalid = Error.Validation(EmailInvalidCode, "The email address is not valid.");

    public static readonly Error NotFound = Error.NotFound(NotFoundCode, "The user was not found.");
}
