using System.Globalization;
using System.Security.Claims;
using ArquitecturaBase.Application.Interfaces.Integrations;
using ArquitecturaBase.Application.Interfaces.Persistence;
using ArquitecturaBase.Application.Models.Identity;
using ArquitecturaBase.Application.Common.Pagination;
using ArquitecturaBase.Application.Models.Roles.ReadModels;
using ArquitecturaBase.Application.Models.Users.ReadModels;
using ArquitecturaBase.Domain.ValueObjects;
using ArquitecturaBase.Infrastructure.Persistence;
using ArquitecturaBase.Infrastructure.Persistence.Extensions;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Identity;
using OpenIddict.Abstractions;

namespace ArquitecturaBase.Infrastructure.Identity;

/// <summary>
/// Operaciones técnicas de Identity y delegaciones en los lectores y en IUserRepository. Toda escritura exige la
/// transacción del caso de uso (IUnitOfWork.ExecuteInTransactionAsync): las de cuentas porque IUserRepository la exige,
/// y las propias (los intentos fallidos y el cierre de sesiones) con su propio chequeo. Las escrituras de roles viven
/// solo en IRoleRepository.
/// </summary>
internal sealed class IdentityService(
    UserManager<ApplicationUser> userManager,
    SignInManager<ApplicationUser> signInManager,
    IOpenIddictAuthorizationManager authorizationManager,
    IOpenIddictTokenManager tokenManager,
    IUserReader userReader,
    IRoleReader roleReader,
    IUserRepository userRepository,
    ApplicationDbContext dbContext)
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

    // SetPhone, RemovePhone y SetEmail solo delegan en IUserRepository, que es el dueño de esas escrituras. SetPhone y
    // SetEmail siguen acá porque LoginCodeVerifier y WhatsAppInboundService todavía las piden por IIdentityService;
    // RemovePhone ya no lo pide ningún servicio, solo los tests. El recorte de los tres está previsto en la Etapa 2 de
    // docs/plans/2026-09-26-plantilla-estandar-por-etapas.md.
    public Task SetPhoneAsync(Guid userId, PhoneNumber phone, bool confirmed, CancellationToken cancellationToken) =>
        userRepository.SetPhoneAsync(userId, phone, confirmed, cancellationToken);

    public Task RemovePhoneAsync(Guid userId, CancellationToken cancellationToken) =>
        userRepository.RemovePhoneAsync(userId, cancellationToken);

    public Task SetEmailAsync(Guid userId, Email email, bool confirmed, CancellationToken cancellationToken) =>
        userRepository.SetEmailAsync(userId, email, confirmed, cancellationToken);

    public async Task<IReadOnlyCollection<string>> GetRolesAsync(Guid userId, CancellationToken cancellationToken)
    {
        await RequireUserAsync(userId, cancellationToken);
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

    public async Task RevokeSessionsAsync(Guid userId, CancellationToken cancellationToken)
    {
        // El stamp y las dos revocaciones tienen que ir juntos. Las revocaciones de OpenIddict son UPDATE inmediatos, y
        // sin la transacción del caso de uso se confirmarían sueltas. Los enlaces pendientes los invalida
        // AccountAccessRevoker, que es quien llama.
        dbContext.RequireTransaction();
        var user = await RequireUserAsync(userId, cancellationToken);

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

    public Task DeleteAsync(Guid userId, CancellationToken cancellationToken) =>
        userRepository.DeleteAsync(userId, cancellationToken);

    public Task<int> CountActiveAdminsAsync(CancellationToken cancellationToken) =>
        userReader.CountActiveAdminsAsync(cancellationToken);

    public async Task<bool> IsLockedOutAsync(Guid userId, CancellationToken cancellationToken) =>
        await userManager.IsLockedOutAsync(await RequireUserAsync(userId, cancellationToken));

    public async Task RegisterFailedAttemptAsync(Guid userId, CancellationToken cancellationToken)
    {
        dbContext.RequireTransaction();

        (await userManager.AccessFailedAsync(await RequireUserAsync(userId, cancellationToken)))
            .EnsureSucceeded("register the failed attempt");
    }

    public async Task ResetFailedAttemptsAsync(Guid userId, CancellationToken cancellationToken)
    {
        dbContext.RequireTransaction();

        (await userManager.ResetAccessFailedCountAsync(await RequireUserAsync(userId, cancellationToken)))
            .EnsureSucceeded("reset the failed attempts");
    }

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

    private async Task<ApplicationUser> RequireUserAsync(Guid userId, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var user = await userManager.FindByIdAsync(userId.ToString("D", CultureInfo.InvariantCulture));
        cancellationToken.ThrowIfCancellationRequested();

        // FindByIdAsync puede devolver una entidad borrada que ya está seguida por EF en este scope.
        return user is { IsDeleted: false }
            ? user
            : throw new InvalidOperationException("The user does not exist.");
    }
}
