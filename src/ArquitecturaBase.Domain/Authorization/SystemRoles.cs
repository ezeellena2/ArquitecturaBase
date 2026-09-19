namespace ArquitecturaBase.Domain.Authorization;

/// <summary>Roles que existen siempre. Admin recibe todos los permisos; User, ninguno por ahora.</summary>
public static class SystemRoles
{
    public const string Admin = "Admin";
    public const string User = "User";

    public static IReadOnlyCollection<string> All { get; } = [Admin, User];
}
