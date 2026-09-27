using ArquitecturaBase.Application.Common.Pagination;
using ArquitecturaBase.Application.Interfaces.Integrations;
using ArquitecturaBase.Application.Interfaces.Persistence;
using ArquitecturaBase.Application.Models.Identity;
using ArquitecturaBase.Application.Models.Roles.ReadModels;
using ArquitecturaBase.Application.Models.Users.ReadModels;
using ArquitecturaBase.Domain.ValueObjects;
using Microsoft.AspNetCore.Identity;

namespace ArquitecturaBase.Infrastructure.Identity;

/// <summary>
/// Transitorio (Etapa 2): reenvía los datos de cuentas a los lectores y a IUserRepository, y lo técnico del ingreso a
/// ISignInService, mientras los servicios y los tests dejan de pedirlo. Se borra en la tarea 16 del plan de la Etapa 2.
/// </summary>
internal sealed class IdentityService(
    UserManager<ApplicationUser> userManager,
    ISignInService signIn,
    IUserReader userReader,
    IRoleReader roleReader,
    IUserRepository userRepository)
    : IIdentityService
{
    public Task<UserAccount?> FindByIdAsync(Guid userId, CancellationToken cancellationToken) =>
        userReader.FindByIdAsync(userId, cancellationToken);

    public Task<UserAccount?> FindByEmailAsync(Email email, CancellationToken cancellationToken) =>
        userReader.FindByEmailAsync(email, cancellationToken);

    public Task<bool> IsDeletedEmailAsync(Email email, CancellationToken cancellationToken) =>
        userReader.IsDeletedEmailAsync(email, cancellationToken);

    public Task<UserAccount?> FindByExternalLoginAsync(string provider, string providerKey, CancellationToken cancellationToken) =>
        userReader.FindByExternalLoginAsync(provider, providerKey, cancellationToken);

    public Task<UserAccount?> FindByPhoneAsync(PhoneNumber phone, CancellationToken cancellationToken) =>
        userReader.FindByPhoneAsync(phone, cancellationToken);

    public Task<bool> IsDeletedPhoneAsync(PhoneNumber phone, CancellationToken cancellationToken) =>
        userReader.IsDeletedPhoneAsync(phone, cancellationToken);

    public Task<UserAccount> CreateAsync(
        Email? email,
        PhoneNumber? phone,
        bool phoneConfirmed,
        string? displayName,
        string culture,
        CancellationToken cancellationToken) =>
        userRepository.CreateAsync(email, phone, phoneConfirmed, displayName, culture, cancellationToken);

    public Task<UserAccount> CreateUnverifiedAsync(
        Email? email,
        PhoneNumber? phone,
        string? displayName,
        string culture,
        CancellationToken cancellationToken) =>
        userRepository.CreateUnverifiedAsync(email, phone, displayName, culture, cancellationToken);

    public Task AddExternalLoginAsync(Guid userId, ExternalLogin login, CancellationToken cancellationToken) =>
        userRepository.AddExternalLoginAsync(userId, login, cancellationToken);

    public Task<bool> HasExternalLoginAsync(Guid userId, string provider, CancellationToken cancellationToken) =>
        userReader.HasExternalLoginAsync(userId, provider, cancellationToken);

    public Task SetPhoneAsync(Guid userId, PhoneNumber phone, bool confirmed, CancellationToken cancellationToken) =>
        userRepository.SetPhoneAsync(userId, phone, confirmed, cancellationToken);

    public Task RemovePhoneAsync(Guid userId, CancellationToken cancellationToken) =>
        userRepository.RemovePhoneAsync(userId, cancellationToken);

    public Task SetEmailAsync(Guid userId, Email email, bool confirmed, CancellationToken cancellationToken) =>
        userRepository.SetEmailAsync(userId, email, confirmed, cancellationToken);

    public async Task<IReadOnlyCollection<string>> GetRolesAsync(Guid userId, CancellationToken cancellationToken)
    {
        await userManager.RequireUserAsync(userId, cancellationToken);
        var roles = await userReader.ListRoleNamesForUserAsync(userId, cancellationToken);

        return [.. roles.Order(StringComparer.Ordinal)];
    }

    public Task<UserAccount?> FindDeletedByEmailAsync(Email email, CancellationToken cancellationToken) =>
        userReader.FindDeletedByEmailAsync(email, cancellationToken);

    public Task<UserAccount?> FindDeletedByPhoneAsync(PhoneNumber phone, CancellationToken cancellationToken) =>
        userReader.FindDeletedByPhoneAsync(phone, cancellationToken);

    public Task RestoreAsync(Guid userId, string? displayName, CancellationToken cancellationToken) =>
        userRepository.RestoreAsync(userId, displayName, cancellationToken);

    public Task SetRolesAsync(Guid userId, IReadOnlyCollection<string> roles, CancellationToken cancellationToken) =>
        userRepository.SetRolesAsync(userId, roles, cancellationToken);

    public Task<IReadOnlyCollection<string>> ListRoleNamesAsync(CancellationToken cancellationToken) =>
        roleReader.ListRoleNamesAsync(cancellationToken);

    public Task<UserDetail?> FindDetailAsync(Guid userId, CancellationToken cancellationToken) =>
        userReader.FindDetailAsync(userId, cancellationToken);

    public Task SetDisplayNameAsync(Guid userId, string? displayName, CancellationToken cancellationToken) =>
        userRepository.SetDisplayNameAsync(userId, displayName, cancellationToken);

    public Task SetActiveAsync(Guid userId, bool isActive, CancellationToken cancellationToken) =>
        userRepository.SetActiveAsync(userId, isActive, cancellationToken);

    public Task RevokeSessionsAsync(Guid userId, CancellationToken cancellationToken) =>
        signIn.RevokeSessionsAsync(userId, cancellationToken);

    public Task DeleteAsync(Guid userId, CancellationToken cancellationToken) =>
        userRepository.DeleteAsync(userId, cancellationToken);

    public Task<int> CountActiveAdminsAsync(CancellationToken cancellationToken) =>
        userReader.CountActiveAdminsAsync(cancellationToken);

    public Task<bool> IsLockedOutAsync(Guid userId, CancellationToken cancellationToken) =>
        signIn.IsLockedOutAsync(userId, cancellationToken);

    public Task RegisterFailedAttemptAsync(Guid userId, CancellationToken cancellationToken) =>
        signIn.RegisterFailedAttemptAsync(userId, cancellationToken);

    public Task ResetFailedAttemptsAsync(Guid userId, CancellationToken cancellationToken) =>
        signIn.ResetFailedAttemptsAsync(userId, cancellationToken);

    public Task SignInAsync(Guid userId, CancellationToken cancellationToken) =>
        signIn.SignInAsync(userId, cancellationToken);

    public Task<ExternalLogin?> GetExternalLoginAsync(CancellationToken cancellationToken) =>
        signIn.GetExternalLoginAsync(cancellationToken);

    public Task SignOutExternalAsync(CancellationToken cancellationToken) =>
        signIn.SignOutExternalAsync(cancellationToken);

    public Task<PagedResult<UserListItem>> ListUsersAsync(UserListRequest request, CancellationToken cancellationToken) =>
        userReader.ListUsersAsync(request, cancellationToken);

    public Task<UserFilterCounts> GetUserFilterCountsAsync(UserListRequest request, CancellationToken cancellationToken) =>
        userReader.GetUserFilterCountsAsync(request, cancellationToken);

    public Task<IReadOnlyCollection<RoleListItem>> ListRolesAsync(CancellationToken cancellationToken) =>
        roleReader.ListRolesAsync(cancellationToken);

    public Task<RoleListItem?> FindRoleAsync(Guid roleId, CancellationToken cancellationToken) =>
        roleReader.FindRoleAsync(roleId, cancellationToken);

    public Task<bool> RoleNameExistsAsync(string name, Guid? excludedRoleId, CancellationToken cancellationToken) =>
        roleReader.RoleNameExistsAsync(name, excludedRoleId, cancellationToken);
}
