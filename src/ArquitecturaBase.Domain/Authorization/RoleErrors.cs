using ArquitecturaBase.Domain.Results;

namespace ArquitecturaBase.Domain.Authorization;

public static class RoleErrors
{
    public const string NotFoundCode = "Roles.Role.NotFound";
    public const string AlreadyExistsCode = "Roles.Role.AlreadyExists";
    public const string SystemRoleCannotChangeCode = "Roles.Role.SystemRoleCannotChange";
    public const string HasUsersCode = "Roles.Role.HasUsers";

    public const string UserCountKey = "userCount";

    public static readonly Error NotFound = Error.NotFound(NotFoundCode, "The role was not found.");

    public static readonly Error AlreadyExists = Error.Conflict(AlreadyExistsCode, "A role with that name already exists.");

    /// <summary>Admin y User no se renombran ni se borran, y a Admin no se le editan los permisos.</summary>
    public static readonly Error SystemRoleCannotChange =
        Error.Forbidden(SystemRoleCannotChangeCode, "System roles can't be renamed, deleted or have their permissions changed.");

    /// <summary>Borrar un rol con usuarios. El metadato dice cuántos son, para que se reasignen primero.</summary>
    public static Error HasUsers(int userCount) =>
        Error.Conflict(
            HasUsersCode,
            "The role still has users assigned.",
            new Dictionary<string, object?> { [UserCountKey] = userCount });
}
