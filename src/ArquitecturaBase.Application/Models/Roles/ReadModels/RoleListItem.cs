namespace ArquitecturaBase.Application.Models.Roles.ReadModels;

/// <summary>Un rol con sus permisos y cuántos usuarios activos lo tienen (sección 6 del spec de la Fase 4).</summary>
public sealed record RoleListItem(
    Guid Id,
    string Name,
    string? Description,
    bool IsSystemRole,
    int UserCount,
    IReadOnlyCollection<string> Permissions);
