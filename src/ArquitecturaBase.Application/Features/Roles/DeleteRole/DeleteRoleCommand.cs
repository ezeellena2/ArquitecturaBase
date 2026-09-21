using ArquitecturaBase.Application.Abstractions.Messaging;

namespace ArquitecturaBase.Application.Features.Roles.DeleteRole;

public sealed record DeleteRoleCommand(Guid RoleId) : ICommand;
