using ArquitecturaBase.Application.Abstractions.Messaging;

namespace ArquitecturaBase.Application.Features.Roles.GetPermissions;

public sealed record GetPermissionsQuery : IQuery<IReadOnlyCollection<PermissionGroup>>;
