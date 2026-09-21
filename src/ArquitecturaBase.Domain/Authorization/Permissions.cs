namespace ArquitecturaBase.Domain.Authorization;

/// <summary>
/// Catálogo de permisos. Cada permiso se guarda como role claim de tipo <see cref="ClaimType"/> y un usuario suma
/// los de todos sus roles. Los endpoints piden permisos, nunca roles.
/// </summary>
public static class Permissions
{
    public const string ClaimType = "permission";

    public static class Users
    {
        public const string Read = "users.read";
        public const string Manage = "users.manage";
    }

    public static class Roles
    {
        public const string Read = "roles.read";
        public const string Manage = "roles.manage";
    }

    public static class Settings
    {
        public const string Manage = "settings.manage";
    }

    public static IReadOnlyCollection<string> All { get; } =
        [Users.Read, Users.Manage, Roles.Read, Roles.Manage, Settings.Manage];
}
