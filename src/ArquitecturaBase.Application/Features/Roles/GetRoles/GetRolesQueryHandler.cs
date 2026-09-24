using ArquitecturaBase.Application.Interfaces.Integrations;
using ArquitecturaBase.Application.Abstractions.Messaging;
using ArquitecturaBase.Domain.Results;

namespace ArquitecturaBase.Application.Features.Roles.GetRoles;

internal sealed class GetRolesQueryHandler(IIdentityService identityService)
    : IQueryHandler<GetRolesQuery, IReadOnlyCollection<RoleListItem>>
{
    public async Task<Result<IReadOnlyCollection<RoleListItem>>> Handle(GetRolesQuery query, CancellationToken cancellationToken) =>
        Result.Success(await identityService.ListRolesAsync(cancellationToken));
}
