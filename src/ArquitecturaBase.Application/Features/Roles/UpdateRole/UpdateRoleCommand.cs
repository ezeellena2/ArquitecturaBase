using ArquitecturaBase.Application.Abstractions.Messaging;

namespace ArquitecturaBase.Application.Features.Roles.UpdateRole;

public sealed record UpdateRoleCommand(
    Guid RoleId,
    string? Name,
    string? Description,
    IReadOnlyCollection<string>? Permissions) : ICommand;
