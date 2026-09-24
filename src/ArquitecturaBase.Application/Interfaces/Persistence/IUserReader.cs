using ArquitecturaBase.Application.Common.Pagination;
using ArquitecturaBase.Application.Features.Users.GetUsers;
using ArquitecturaBase.Application.Models.Identity;

namespace ArquitecturaBase.Application.Interfaces.Persistence;

/// <summary>Consultas de listado y conteos de usuarios con los mismos filtros.</summary>
public interface IUserReader
{
    Task<PagedResult<UserListItem>> ListUsersAsync(UserListRequest request, CancellationToken cancellationToken);

    Task<UserFilterCounts> GetUserFilterCountsAsync(UserListRequest request, CancellationToken cancellationToken);
}
