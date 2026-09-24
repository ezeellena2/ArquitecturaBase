namespace ArquitecturaBase.Application.Models.Roles;

public sealed record CreateRoleRequest(
    string? Name,
    string? Description,
    IReadOnlyCollection<string>? Permissions);
