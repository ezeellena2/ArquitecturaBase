using System.Globalization;
using System.Security.Claims;
using ArquitecturaBase.Application.Common.Exceptions;
using ArquitecturaBase.Application.Interfaces.Integrations;
using ArquitecturaBase.Application.Interfaces.Persistence;
using ArquitecturaBase.Application.Models.Identity;
using ArquitecturaBase.Application.Common.Pagination;
using ArquitecturaBase.Application.Features.Roles.GetRoles;
using ArquitecturaBase.Application.Features.Users.GetUser;
using ArquitecturaBase.Application.Features.Users.GetUsers;
using ArquitecturaBase.Domain.Authorization;
using ArquitecturaBase.Domain.ValueObjects;
using ArquitecturaBase.Infrastructure.Persistence;
using ArquitecturaBase.Infrastructure.Persistence.Extensions;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using OpenIddict.Abstractions;

namespace ArquitecturaBase.Infrastructure.Identity;

internal sealed class IdentityService(
    UserManager<ApplicationUser> userManager,
    SignInManager<ApplicationUser> signInManager,
    RoleManager<ApplicationRole> roleManager,
    ApplicationDbContext dbContext,
    IOpenIddictAuthorizationManager authorizationManager,
    IOpenIddictTokenManager tokenManager,
    IInitialAdmin initialAdmin,
    ILoginLinkRepository loginLinks,
    IUserReader userReader,
    IRoleReader roleReader,
    TimeProvider timeProvider)
    : IIdentityService
{
    public async Task<UserAccount?> FindByIdAsync(Guid userId, CancellationToken cancellationToken) =>
        ToAccountOrNull(await FindUserAsync(userId, cancellationToken));

    public async Task<UserAccount?> FindByEmailAsync(Email email, CancellationToken cancellationToken) =>
        ToAccountOrNull(await userManager.FindByEmailAsync(email.Value));

    public Task<bool> IsDeletedEmailAsync(Email email, CancellationToken cancellationToken)
    {
        var normalized = userManager.NormalizeEmail(email.Value);

        return userManager.Users
            .IgnoreQueryFilters([ModelBuilderExtensions.SoftDeleteFilter])
            .AnyAsync(user => user.IsDeleted && user.NormalizedEmail == normalized, cancellationToken);
    }

    public async Task<UserAccount?> FindByExternalLoginAsync(string provider, string providerKey, CancellationToken cancellationToken) =>
        ToAccountOrNull(await userManager.FindByLoginAsync(provider, providerKey));

    // Compara por la columna, que tiene índice único. El filtro global deja afuera a las cuentas borradas.
    public async Task<UserAccount?> FindByPhoneAsync(PhoneNumber phone, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(phone);

        return ToAccountOrNull(await userManager.Users
            .FirstOrDefaultAsync(user => user.PhoneNumber == phone.Value, cancellationToken));
    }

    public Task<bool> IsDeletedPhoneAsync(PhoneNumber phone, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(phone);

        return userManager.Users
            .IgnoreQueryFilters([ModelBuilderExtensions.SoftDeleteFilter])
            .AnyAsync(user => user.IsDeleted && user.PhoneNumber == phone.Value, cancellationToken);
    }

    // Los dos ingresos verifican el correo antes de crear la cuenta.
    public async Task<UserAccount> CreateAsync(
        Email? email,
        PhoneNumber? phone,
        bool phoneConfirmed,
        string? displayName,
        string culture,
        CancellationToken cancellationToken) =>
        await CreateUserAsync(NewUser(email, emailConfirmed: email is not null, phone, phoneConfirmed, displayName, culture), email);

    // Lo que carga un administrador queda sin verificar hasta que la persona entra con eso.
    public async Task<UserAccount> CreateUnverifiedAsync(
        Email? email,
        PhoneNumber? phone,
        string? displayName,
        string culture,
        CancellationToken cancellationToken)
    {
        var user = NewUser(email, emailConfirmed: false, phone, phoneConfirmed: false, displayName, culture);

        // Como UpdateUniqueValueAsync: el alta buscó antes el correo y el número, pero el bot y Google crean cuentas sin
        // el lock del destino, y una puede confirmarse entre esa búsqueda y este guardado. EF deshace solo este guardado
        // (con un savepoint, si hay una transacción abierta) y la cuenta sale del change tracker: si la unidad de trabajo
        // guarda después, no la vuelve a intentar.
        try
        {
            return await CreateUserAsync(user, email);
        }
        catch (DbUpdateException exception) when (UniqueViolations.Translate(exception) is { } unique)
        {
            dbContext.Entry(user).State = EntityState.Detached;

            throw unique;
        }
    }

    private static ApplicationUser NewUser(
        Email? email,
        bool emailConfirmed,
        PhoneNumber? phone,
        bool phoneConfirmed,
        string? displayName,
        string culture)
    {
        if (email is null && phone is null)
        {
            throw new ArgumentException("An account needs an email or a phone number.", nameof(email));
        }

        var user = new ApplicationUser
        {
            Email = email?.Value,
            EmailConfirmed = email is not null && emailConfirmed,
            PhoneNumber = phone?.Value,
            PhoneNumberConfirmed = phone is not null && phoneConfirmed,
            DisplayName = TrimDisplayName(displayName),
            Culture = culture,
        };

        // El UserName es el Id (sección 6.1 del spec del ingreso con WhatsApp): una cuenta sin correo igual necesita
        // uno único, y usar el correo o el número haría que cambiar uno cambie el otro. El constructor ya generó el Id.
        user.UserName = user.Id.ToString("D", CultureInfo.InvariantCulture);

        return user;
    }

    private async Task<UserAccount> CreateUserAsync(ApplicationUser user, Email? email)
    {
        (await userManager.CreateAsync(user)).EnsureSucceeded("create the user");
        (await userManager.AddToRoleAsync(user, IsAdminEmail(email) ? SystemRoles.Admin : SystemRoles.User))
            .EnsureSucceeded("assign the initial role");

        return ToAccount(user);
    }

    public async Task AddExternalLoginAsync(Guid userId, ExternalLogin login, CancellationToken cancellationToken)
    {
        var user = await RequireUserAsync(userId, cancellationToken);

        (await userManager.AddLoginAsync(user, new UserLoginInfo(login.Provider, login.ProviderKey, login.Provider)))
            .EnsureSucceeded("link the external login");
    }

    public Task<bool> HasExternalLoginAsync(Guid userId, string provider, CancellationToken cancellationToken) =>
        dbContext.UserLogins.AnyAsync(login => login.UserId == userId && login.LoginProvider == provider, cancellationToken);

    // Los tres escriben las propiedades y guardan con UpdateAsync, que recalcula el correo normalizado. No usan
    // SetPhoneNumberAsync ni SetEmailAsync de UserManager: esos renuevan el security stamp, y la cookie de quien vincula
    // su propio número desde el perfil dejaría de valer en la próxima petición. Cortar las sesiones lo decide quien llama.
    public async Task SetPhoneAsync(Guid userId, PhoneNumber phone, bool confirmed, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(phone);

        var user = await RequireUserAsync(userId, cancellationToken);
        user.PhoneNumber = phone.Value;
        user.PhoneNumberConfirmed = confirmed;

        await UpdateUniqueValueAsync(user, "set the phone number");
    }

    public async Task RemovePhoneAsync(Guid userId, CancellationToken cancellationToken)
    {
        var user = await RequireUserAsync(userId, cancellationToken);
        user.PhoneNumber = null;
        user.PhoneNumberConfirmed = false;

        (await userManager.UpdateAsync(user)).EnsureSucceeded("remove the phone number");
    }

    public async Task SetEmailAsync(Guid userId, Email email, bool confirmed, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(email);

        var user = await RequireUserAsync(userId, cancellationToken);
        user.Email = email.Value;
        user.EmailConfirmed = confirmed;

        await UpdateUniqueValueAsync(user, "set the email");
    }

    /// <summary>
    /// Guarda un número o un correo, que tienen índice único. Quien llama ya se fijó que no fuera de otra cuenta, pero
    /// otra puede haberlo guardado entre esa búsqueda y este guardado. Identity guarda en el acto: si choca, EF deshace
    /// solo este guardado (con un savepoint, si hay una transacción abierta), la cuenta vuelve a como estaba en la base
    /// y se lanza <see cref="UniqueConstraintViolationException"/>. Así el resto de la unidad de trabajo, por ejemplo el
    /// código recién gastado, se puede guardar igual, sin volver a intentar este cambio.
    /// </summary>
    private async Task UpdateUniqueValueAsync(ApplicationUser user, string operation)
    {
        try
        {
            (await userManager.UpdateAsync(user)).EnsureSucceeded(operation);
        }
        catch (DbUpdateException exception) when (UniqueViolations.Translate(exception) is { } unique)
        {
            var entry = dbContext.Entry(user);
            entry.CurrentValues.SetValues(entry.OriginalValues);
            entry.State = EntityState.Unchanged;

            throw unique;
        }
    }

    public async Task<IReadOnlyCollection<string>> GetRolesAsync(Guid userId, CancellationToken cancellationToken)
    {
        var roles = await userManager.GetRolesAsync(await RequireUserAsync(userId, cancellationToken));

        return [.. roles.Order(StringComparer.Ordinal)];
    }

    public async Task<UserAccount?> FindDeletedByEmailAsync(Email email, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(email);

        var normalized = userManager.NormalizeEmail(email.Value);

        // Mismo estilo que IsDeletedEmailAsync (Tarea 6): se saltea solo el filtro del borrado lógico.
        return ToAccountOrNull(await userManager.Users
            .AsNoTracking()
            .IgnoreQueryFilters([ModelBuilderExtensions.SoftDeleteFilter])
            .FirstOrDefaultAsync(user => user.IsDeleted && user.NormalizedEmail == normalized, cancellationToken));
    }

    // Como FindDeletedByEmailAsync, por la columna del número, que tiene índice único también para las borradas.
    public async Task<UserAccount?> FindDeletedByPhoneAsync(PhoneNumber phone, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(phone);

        return ToAccountOrNull(await userManager.Users
            .AsNoTracking()
            .IgnoreQueryFilters([ModelBuilderExtensions.SoftDeleteFilter])
            .FirstOrDefaultAsync(user => user.IsDeleted && user.PhoneNumber == phone.Value, cancellationToken));
    }

    public async Task RestoreAsync(Guid userId, string? displayName, CancellationToken cancellationToken)
    {
        var user = await userManager.Users
            .IgnoreQueryFilters([ModelBuilderExtensions.SoftDeleteFilter])
            .FirstOrDefaultAsync(user => user.Id == userId, cancellationToken)
            ?? throw new InvalidOperationException("The user does not exist.");

        // ApplicationUser.Restore(), que dejó la Tarea 6, limpia IsDeleted, DeletedAtUtc y DeletedBy.
        user.Restore();
        user.IsActive = true;
        user.DisplayName = TrimDisplayName(displayName);

        // El bloqueo por códigos fallidos se limpia: una cuenta que se bloqueó y después se eliminó tiene que
        // volver usable. Si no, la persona recibe Auth.Account.LockedOut al intentar entrar y el administrador
        // no tiene desde dónde destrabarla: el alta la restaura, pero con el bloqueo puesto.
        user.AccessFailedCount = 0;
        user.LockoutEnd = null;

        (await userManager.UpdateAsync(user)).EnsureSucceeded("restore the user");
    }

    public async Task SetRolesAsync(Guid userId, IReadOnlyCollection<string> roles, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(roles);

        var user = await RequireUserAsync(userId, cancellationToken);
        var current = await userManager.GetRolesAsync(user);

        var removed = current.Except(roles, StringComparer.Ordinal).ToList();

        if (removed.Count > 0)
        {
            (await userManager.RemoveFromRolesAsync(user, removed)).EnsureSucceeded("remove the roles");
        }

        var added = roles.Except(current, StringComparer.Ordinal).ToList();

        if (added.Count > 0)
        {
            (await userManager.AddToRolesAsync(user, added)).EnsureSucceeded("assign the roles");
        }
    }

    public Task<IReadOnlyCollection<string>> ListRoleNamesAsync(CancellationToken cancellationToken) =>
        roleReader.ListRoleNamesAsync(cancellationToken);

    public async Task<UserDetail?> FindDetailAsync(Guid userId, CancellationToken cancellationToken)
    {
        var detail = await dbContext.Users
            .AsNoTracking()
            .Where(user => user.Id == userId)
            .Select(user => new
            {
                user.Id,
                user.Email,
                user.EmailConfirmed,
                user.PhoneNumber,
                user.PhoneNumberConfirmed,
                user.DisplayName,
                user.IsActive,
                user.CreatedAtUtc,
                Roles = dbContext.Roles
                    .Where(role => dbContext.UserRoles.Any(userRole => userRole.UserId == user.Id && userRole.RoleId == role.Id))
                    .Select(role => role.Name!)
                    .ToList(),
            })
            .FirstOrDefaultAsync(cancellationToken);

        return detail is null
            ? null
            : new UserDetail(
                detail.Id,
                detail.Email,
                detail.EmailConfirmed,
                detail.PhoneNumber,
                detail.PhoneNumberConfirmed,
                detail.DisplayName,
                detail.IsActive,
                detail.CreatedAtUtc,
                [.. detail.Roles.Order(StringComparer.Ordinal)]);
    }

    public async Task SetDisplayNameAsync(Guid userId, string? displayName, CancellationToken cancellationToken)
    {
        var user = await RequireUserAsync(userId, cancellationToken);
        user.DisplayName = TrimDisplayName(displayName);

        (await userManager.UpdateAsync(user)).EnsureSucceeded("update the display name");
    }

    public async Task UpdateProfileAsync(
        Guid userId, string? displayName, string culture, string timeZoneId, CancellationToken cancellationToken)
    {
        var user = await RequireUserAsync(userId, cancellationToken);
        user.DisplayName = TrimDisplayName(displayName);
        user.Culture = culture;
        user.TimeZoneId = timeZoneId;

        (await userManager.UpdateAsync(user)).EnsureSucceeded("update the profile");
    }

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

    public async Task<int> CountActiveAdminsAsync(CancellationToken cancellationToken) =>
        (await userManager.GetUsersInRoleAsync(SystemRoles.Admin)).Count(user => user.IsActive);

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

    // Una cuenta de solo número nunca es la del administrador del seed, que se reconoce por el correo.
    private bool IsAdminEmail(Email? email) => email is not null && initialAdmin.IsInitialAdmin(email);

    private Task<ApplicationUser?> FindUserAsync(Guid userId, CancellationToken cancellationToken) =>
        userManager.Users.FirstOrDefaultAsync(user => user.Id == userId, cancellationToken);

    private async Task<ApplicationUser> RequireUserAsync(Guid userId, CancellationToken cancellationToken) =>
        await FindUserAsync(userId, cancellationToken) ?? throw new InvalidOperationException("The user does not exist.");

    private static string? TrimDisplayName(string? displayName) =>
        displayName is { Length: > ApplicationUser.DisplayNameMaxLength }
            ? displayName[..ApplicationUser.DisplayNameMaxLength]
            : displayName;

    private static UserAccount? ToAccountOrNull(ApplicationUser? user) => user is null ? null : ToAccount(user);

    private static UserAccount ToAccount(ApplicationUser user) =>
        new(
            user.Id,
            user.Email,
            user.EmailConfirmed,
            user.PhoneNumber,
            user.PhoneNumberConfirmed,
            user.DisplayName,
            user.Culture,
            user.TimeZoneId,
            user.IsActive);
}
