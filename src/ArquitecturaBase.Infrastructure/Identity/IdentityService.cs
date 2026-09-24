using System.Globalization;
using System.Security.Claims;
using ArquitecturaBase.Application.Interfaces.Integrations;
using ArquitecturaBase.Application.Interfaces.Persistence;
using ArquitecturaBase.Application.Models.Identity;
using ArquitecturaBase.Application.Common.Pagination;
using ArquitecturaBase.Application.Features.Roles.GetRoles;
using ArquitecturaBase.Application.Features.Users.GetUser;
using ArquitecturaBase.Application.Features.Users.GetUsers;
using ArquitecturaBase.Domain.ValueObjects;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using OpenIddict.Abstractions;

namespace ArquitecturaBase.Infrastructure.Identity;

internal sealed class IdentityService(
    UserManager<ApplicationUser> userManager,
    SignInManager<ApplicationUser> signInManager,
    RoleManager<ApplicationRole> roleManager,
    IOpenIddictAuthorizationManager authorizationManager,
    IOpenIddictTokenManager tokenManager,
    ILoginLinkRepository loginLinks,
    IUserReader userReader,
    IRoleReader roleReader,
    IUserRepository userRepository,
    TimeProvider timeProvider)
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

    // Los dos ingresos verifican el correo antes de crear la cuenta.
    public Task<UserAccount> CreateAsync(
        Email? email,
        PhoneNumber? phone,
        bool phoneConfirmed,
        string? displayName,
        string culture,
        CancellationToken cancellationToken) =>
        userRepository.CreateAsync(email, phone, phoneConfirmed, displayName, culture, cancellationToken);

    // Lo que carga un administrador queda sin verificar hasta que la persona entra con eso.
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

    // Adaptación temporal para los consumidores de IIdentityService que todavía no migraron a servicios y repositorios.
    public Task SetPhoneAsync(Guid userId, PhoneNumber phone, bool confirmed, CancellationToken cancellationToken) =>
        userRepository.SetPhoneAsync(userId, phone, confirmed, cancellationToken);

    public async Task RemovePhoneAsync(Guid userId, CancellationToken cancellationToken)
    {
        var user = await RequireUserAsync(userId, cancellationToken);
        user.PhoneNumber = null;
        user.PhoneNumberConfirmed = false;

        (await userManager.UpdateAsync(user)).EnsureSucceeded("remove the phone number");
    }

    public Task SetEmailAsync(Guid userId, Email email, bool confirmed, CancellationToken cancellationToken) =>
        userRepository.SetEmailAsync(userId, email, confirmed, cancellationToken);

    public async Task<IReadOnlyCollection<string>> GetRolesAsync(Guid userId, CancellationToken cancellationToken)
    {
        var roles = await userManager.GetRolesAsync(await RequireUserAsync(userId, cancellationToken));

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

    public async Task SetActiveAsync(Guid userId, bool isActive, CancellationToken cancellationToken)
    {
        var user = await RequireUserAsync(userId, cancellationToken);
        user.IsActive = isActive;

        (await userManager.UpdateAsync(user)).EnsureSucceeded("update the account status");
    }

    public async Task RevokeSessionsAsync(Guid userId, CancellationToken cancellationToken)
    {
        var user = await RequireUserAsync(userId, cancellationToken);

        // Los enlaces de ingreso pendientes también: uno que el bot mandó antes del corte no puede volver a servir si
        // la cuenta se reactiva dentro de sus 10 minutos, ni después de desvincular un número cuyo chat puede no ser
        // más de esta persona. Van antes del security stamp, que guarda con el mismo contexto.
        var nowUtc = timeProvider.GetUtcNow().UtcDateTime;
        var pendingLinks = await loginLinks.ListPendingAsync(userId, cancellationToken);

        foreach (var link in pendingLinks)
        {
            link.Invalidate(nowUtc);
        }

        // La cookie de Identity deja de valer en la próxima petición: el validador del security stamp la rechaza
        // (ValidationInterval está en cero, ver IdentityRegistration).
        (await userManager.UpdateSecurityStampAsync(user)).EnsureSucceeded("renew the security stamp");

        // El subject es el mismo que pone OpenIdPrincipalFactory en el claim "sub".
        var subject = userId.ToString("D", CultureInfo.InvariantCulture);

        // Primero las autorizaciones y después los tokens: si entre las dos llamadas se emitiera un token a partir
        // de una autorización que ya está revocada, la segunda llamada igual lo alcanza. Con
        // EnableTokenEntryValidation, un token revocado deja de valer en el acto, sin esperar a que venza.
        await authorizationManager.RevokeBySubjectAsync(subject, cancellationToken);
        await tokenManager.RevokeBySubjectAsync(subject, cancellationToken);
    }

    // userManager.DeleteAsync marca la entidad como borrada y SoftDeleteInterceptor la convierte en una modificación.
    public async Task DeleteAsync(Guid userId, CancellationToken cancellationToken) =>
        (await userManager.DeleteAsync(await RequireUserAsync(userId, cancellationToken))).EnsureSucceeded("delete the user");

    public Task<int> CountActiveAdminsAsync(CancellationToken cancellationToken) =>
        userReader.CountActiveAdminsAsync(cancellationToken);

    public async Task<bool> IsLockedOutAsync(Guid userId, CancellationToken cancellationToken) =>
        await userManager.IsLockedOutAsync(await RequireUserAsync(userId, cancellationToken));

    public async Task RegisterFailedAttemptAsync(Guid userId, CancellationToken cancellationToken) =>
        (await userManager.AccessFailedAsync(await RequireUserAsync(userId, cancellationToken)))
            .EnsureSucceeded("register the failed attempt");

    public async Task ResetFailedAttemptsAsync(Guid userId, CancellationToken cancellationToken) =>
        (await userManager.ResetAccessFailedCountAsync(await RequireUserAsync(userId, cancellationToken)))
            .EnsureSucceeded("reset the failed attempts");

    public async Task SignInAsync(Guid userId, CancellationToken cancellationToken) =>
        await signInManager.SignInAsync(await RequireUserAsync(userId, cancellationToken), isPersistent: true);

    public async Task<ExternalLogin?> GetExternalLoginAsync(CancellationToken cancellationToken)
    {
        var info = await signInManager.GetExternalLoginInfoAsync();

        if (info is null)
        {
            return null;
        }

        return new ExternalLogin(
            info.LoginProvider,
            info.ProviderKey,
            info.Principal.FindFirstValue(ClaimTypes.Email),
            string.Equals(info.Principal.FindFirstValue(ExternalClaimTypes.EmailVerified), "true", StringComparison.OrdinalIgnoreCase),
            info.Principal.FindFirstValue(ClaimTypes.Name));
    }

    public Task SignOutExternalAsync(CancellationToken cancellationToken) =>
        signInManager.Context.SignOutAsync(IdentityConstants.ExternalScheme);

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

    public async Task<Guid> CreateRoleAsync(
        string name, string? description, IReadOnlyCollection<string> permissions, CancellationToken cancellationToken)
    {
        var role = new ApplicationRole(name) { Description = description };

        (await roleManager.CreateAsync(role)).EnsureSucceeded("create the role");
        await SetRolePermissionsAsync(role, permissions);

        return role.Id;
    }

    public async Task UpdateRoleAsync(
        Guid roleId, string name, string? description, IReadOnlyCollection<string> permissions, CancellationToken cancellationToken)
    {
        var role = await RequireRoleAsync(roleId, cancellationToken);
        role.Description = description;

        // SetRoleNameAsync escribe el nombre y el normalizado en el store; UpdateAsync es el que guarda.
        (await roleManager.SetRoleNameAsync(role, name)).EnsureSucceeded("rename the role");
        (await roleManager.UpdateAsync(role)).EnsureSucceeded("update the role");

        await SetRolePermissionsAsync(role, permissions);
    }

    public async Task DeleteRoleAsync(Guid roleId, CancellationToken cancellationToken) =>
        (await roleManager.DeleteAsync(await RequireRoleAsync(roleId, cancellationToken))).EnsureSucceeded("delete the role");

    private async Task SetRolePermissionsAsync(ApplicationRole role, IReadOnlyCollection<string> permissions)
    {
        ArgumentNullException.ThrowIfNull(permissions);

        var current = (await roleManager.GetClaimsAsync(role))
            .Where(claim => claim.Type == Domain.Authorization.Permissions.ClaimType)
            .ToList();

        foreach (var claim in current.Where(claim => !permissions.Contains(claim.Value, StringComparer.Ordinal)))
        {
            (await roleManager.RemoveClaimAsync(role, claim)).EnsureSucceeded("remove a permission");
        }

        var kept = current.Select(claim => claim.Value).ToHashSet(StringComparer.Ordinal);

        foreach (var permission in permissions.Where(permission => !kept.Contains(permission)))
        {
            (await roleManager.AddClaimAsync(role, new Claim(Domain.Authorization.Permissions.ClaimType, permission)))
                .EnsureSucceeded("add a permission");
        }
    }

    private async Task<ApplicationRole> RequireRoleAsync(Guid roleId, CancellationToken cancellationToken) =>
        await roleManager.Roles.FirstOrDefaultAsync(role => role.Id == roleId, cancellationToken)
            ?? throw new InvalidOperationException("The role does not exist.");

    private Task<ApplicationUser?> FindUserAsync(Guid userId, CancellationToken cancellationToken) =>
        userManager.Users.FirstOrDefaultAsync(user => user.Id == userId, cancellationToken);

    private async Task<ApplicationUser> RequireUserAsync(Guid userId, CancellationToken cancellationToken) =>
        await FindUserAsync(userId, cancellationToken) ?? throw new InvalidOperationException("The user does not exist.");

}
