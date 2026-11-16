namespace ArquitecturaBase.Application.Models.Roles;

/// <summary>La edición de un rol, si sigue en <see cref="ExpectedRevision"/>, la revisión que se leyó (N2).</summary>
public sealed record UpdateRoleRequest(
    Guid RoleId,
    string? Name,
    string? Description,
    IReadOnlyCollection<string>? Permissions,
    long ExpectedRevision);
