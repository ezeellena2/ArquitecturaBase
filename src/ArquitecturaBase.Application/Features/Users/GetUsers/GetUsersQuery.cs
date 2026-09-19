using ArquitecturaBase.Application.Abstractions.Messaging;
using ArquitecturaBase.Application.Common.Pagination;

namespace ArquitecturaBase.Application.Features.Users.GetUsers;

public sealed record GetUsersQuery : PagedRequest, IQuery<PagedResult<UserListItem>>
{
    // Lista blanca: los mismos nombres que usa IdentityService para ordenar.
    public static readonly IReadOnlyCollection<string> SortableFields = ["email", "displayName", "createdAtUtc"];
}
