namespace ArquitecturaBase.Application.Models.Roles;

public sealed record UpdateRoleRequest(
    Guid RoleId,
    string? Name,
    string? Description,
    IReadOnlyCollection<string>? Permissions);
