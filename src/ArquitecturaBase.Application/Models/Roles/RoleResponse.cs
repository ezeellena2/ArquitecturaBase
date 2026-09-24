namespace ArquitecturaBase.Application.Models.Roles;

/// <summary>Rol de la administración con sus permisos y la cantidad de usuarios no borrados.</summary>
public sealed record RoleResponse(
    Guid Id,
    string Name,
    string? Description,
    bool IsSystemRole,
    int UserCount,
    IReadOnlyCollection<string> Permissions);
