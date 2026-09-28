using ArquitecturaBase.Application.Common.Pagination;
using ArquitecturaBase.Application.Models.Users;
using ArquitecturaBase.Domain.Results;

namespace ArquitecturaBase.Application.Interfaces.Services;

public interface IUserQueryService
{
    Task<Result<PagedResult<UserListItemResponse>>> ListUsersAsync(ListUsersRequest request, CancellationToken cancellationToken);

    Task<Result<UserFilterCounts>> GetUserFilterCountsAsync(ListUsersRequest request, CancellationToken cancellationToken);

    Task<Result<UserDetailResponse>> GetUserAsync(Guid userId, CancellationToken cancellationToken);
}
