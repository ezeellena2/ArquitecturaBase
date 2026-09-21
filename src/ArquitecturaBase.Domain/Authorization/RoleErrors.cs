using ArquitecturaBase.Domain.Results;

namespace ArquitecturaBase.Domain.Authorization;

public static class RoleErrors
{
    public const string NotFoundCode = "Roles.Role.NotFound";

    public static readonly Error NotFound = Error.NotFound(NotFoundCode, "The role was not found.");
}
