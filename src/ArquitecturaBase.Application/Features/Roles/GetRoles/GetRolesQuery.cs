using ArquitecturaBase.Application.Abstractions.Messaging;

namespace ArquitecturaBase.Application.Features.Roles.GetRoles;

public sealed record GetRolesQuery : IQuery<IReadOnlyCollection<RoleListItem>>;
