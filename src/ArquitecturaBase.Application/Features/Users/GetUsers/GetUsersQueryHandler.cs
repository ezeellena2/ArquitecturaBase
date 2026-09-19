using ArquitecturaBase.Application.Abstractions.Identity;
using ArquitecturaBase.Application.Abstractions.Messaging;
using ArquitecturaBase.Application.Common.Pagination;
using ArquitecturaBase.Domain.Results;

namespace ArquitecturaBase.Application.Features.Users.GetUsers;

internal sealed class GetUsersQueryHandler(IIdentityService identityService)
    : IQueryHandler<GetUsersQuery, PagedResult<UserListItem>>
{
    public async Task<Result<PagedResult<UserListItem>>> Handle(GetUsersQuery query, CancellationToken cancellationToken) =>
        await identityService.ListUsersAsync(query, cancellationToken);
}
