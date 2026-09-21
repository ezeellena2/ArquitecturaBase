using ArquitecturaBase.Application.Abstractions.Messaging;

namespace ArquitecturaBase.Application.Features.Roles.CreateRole;

public sealed record CreateRoleCommand(string? Name, string? Description, IReadOnlyCollection<string>? Permissions)
    : ICommand<Guid>;
