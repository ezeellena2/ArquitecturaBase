namespace ArquitecturaBase.Application.Models.Roles;

/// <summary>
/// Rol de la administración con sus permisos y la cantidad de usuarios no borrados. <see cref="Revision"/> va de vuelta
/// en <c>ExpectedRevision</c> al guardar: si el rol cambió en el medio, la edición responde 409 (N2).
/// </summary>
public sealed record RoleResponse(
    Guid Id,
    string Name,
    string? Description,
    bool IsSystemRole,
    int UserCount,
    IReadOnlyCollection<string> Permissions,
    long Revision);
