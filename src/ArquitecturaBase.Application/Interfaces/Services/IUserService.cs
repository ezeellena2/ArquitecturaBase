using ArquitecturaBase.Application.Common.Pagination;
using ArquitecturaBase.Application.Models.Identity;
using ArquitecturaBase.Application.Models.Users;
using ArquitecturaBase.Domain.Results;

namespace ArquitecturaBase.Application.Interfaces.Services;

public interface IUserService
{
    Task<Result<PagedResult<UserListItem>>> ListUsersAsync(ListUsersRequest request, CancellationToken cancellationToken);

    Task<Result<UserFilterCounts>> GetUserFilterCountsAsync(UserFilterCountsRequest request, CancellationToken cancellationToken);

    Task<Result<UserDetail>> GetUserAsync(Guid userId, CancellationToken cancellationToken);

    Task<Result<Guid>> CreateUserAsync(CreateUserRequest request, CancellationToken cancellationToken);

    Task<Result> UpdateUserAsync(UpdateUserRequest request, CancellationToken cancellationToken);

    Task<Result> SendInvitationAsync(SendUserInvitationRequest request, CancellationToken cancellationToken);
}
