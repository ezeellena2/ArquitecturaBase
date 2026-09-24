using ArquitecturaBase.Application.Common.Pagination;
using ArquitecturaBase.Application.Features.Users.GetUsers;
using ArquitecturaBase.Application.Features.Users.GetUser;
using ArquitecturaBase.Application.Models.Identity;
using ArquitecturaBase.Domain.ValueObjects;

namespace ArquitecturaBase.Application.Interfaces.Persistence;

/// <summary>Consultas de cuentas, detalle, listado y conteos de usuarios.</summary>
public interface IUserReader
{
    Task<UserAccount?> FindByIdAsync(Guid userId, CancellationToken cancellationToken);

    Task<UserAccount?> FindByEmailAsync(Email email, CancellationToken cancellationToken);

    Task<UserAccount?> FindByPhoneAsync(PhoneNumber phone, CancellationToken cancellationToken);

    Task<bool> IsDeletedEmailAsync(Email email, CancellationToken cancellationToken);

    Task<bool> IsDeletedPhoneAsync(PhoneNumber phone, CancellationToken cancellationToken);

    Task<UserAccount?> FindDeletedByEmailAsync(Email email, CancellationToken cancellationToken);

    Task<UserAccount?> FindDeletedByPhoneAsync(PhoneNumber phone, CancellationToken cancellationToken);

    Task<bool> HasExternalLoginAsync(Guid userId, string provider, CancellationToken cancellationToken);

    /// <summary>Los roles asignados a una cuenta, sin imponer un orden de presentación.</summary>
    Task<IReadOnlyCollection<string>> ListRoleNamesForUserAsync(Guid userId, CancellationToken cancellationToken);

    Task<UserDetail?> FindDetailAsync(Guid userId, CancellationToken cancellationToken);

    Task<int> CountActiveAdminsAsync(CancellationToken cancellationToken);

    Task<PagedResult<UserListItem>> ListUsersAsync(UserListRequest request, CancellationToken cancellationToken);

    Task<UserFilterCounts> GetUserFilterCountsAsync(UserListRequest request, CancellationToken cancellationToken);
}
