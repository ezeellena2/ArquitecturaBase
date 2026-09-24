using System.Globalization;
using ArquitecturaBase.Application.Interfaces.Integrations;
using ArquitecturaBase.Application.Interfaces.Persistence;
using ArquitecturaBase.Application.Models.Identity;
using ArquitecturaBase.Domain.Authorization;
using ArquitecturaBase.Domain.ValueObjects;
using ArquitecturaBase.Infrastructure.Identity;
using ArquitecturaBase.Infrastructure.Persistence.Extensions;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace ArquitecturaBase.Infrastructure.Persistence.Repositories;

/// <summary>
/// Escrituras de cuentas con UserManager. No crea una transacción propia: los casos de uso que necesitan atomicidad
/// toman sus locks antes de llamar al repositorio y confirman con IUnitOfWork. Los consumidores heredados conservan
/// el autoguardado de Identity cuando invocan una operación aislada.
/// </summary>
internal sealed class UserRepository(
    UserManager<ApplicationUser> userManager,
    ApplicationDbContext dbContext,
    IInitialAdmin initialAdmin) : IUserRepository
{
    public async Task<UserAccount> CreateAsync(
        Email? email,
        PhoneNumber? phone,
        bool phoneConfirmed,
        string? displayName,
        string culture,
        CancellationToken cancellationToken) =>
        await CreateUserAsync(NewUser(email, emailConfirmed: email is not null, phone, phoneConfirmed, displayName, culture), email);

    public async Task<UserAccount> CreateUnverifiedAsync(
        Email? email,
        PhoneNumber? phone,
        string? displayName,
        string culture,
        CancellationToken cancellationToken)
    {
        var user = NewUser(email, emailConfirmed: false, phone, phoneConfirmed: false, displayName, culture);

        // Google y el bot pueden crear una cuenta sin el lock del destino. Si chocan con el índice único, EF revierte
        // este guardado (savepoint en la transacción del caso de uso) y se despega la entidad para no reintentarla.
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

    public async Task AddExternalLoginAsync(Guid userId, ExternalLogin login, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(login);
        var user = await RequireUserAsync(userId, cancellationToken);

        (await userManager.AddLoginAsync(user, new UserLoginInfo(login.Provider, login.ProviderKey, login.Provider)))
            .EnsureSucceeded("link the external login");
    }

    public async Task RestoreAsync(Guid userId, string? displayName, CancellationToken cancellationToken)
    {
        var user = await userManager.Users
            .IgnoreQueryFilters([ModelBuilderExtensions.SoftDeleteFilter])
            .FirstOrDefaultAsync(user => user.Id == userId, cancellationToken)
            ?? throw new InvalidOperationException("The user does not exist.");

        user.Restore();
        user.IsActive = true;
        user.DisplayName = ApplicationUserMapper.TrimDisplayName(displayName);
        user.AccessFailedCount = 0;
        user.LockoutEnd = null;

        (await userManager.UpdateAsync(user)).EnsureSucceeded("restore the user");
    }

    // No usamos SetEmailAsync/SetPhoneNumberAsync de UserManager: renuevan el security stamp. Quien necesita cortar
    // sesiones invoca la revocación aparte, después de estas escrituras.
    public async Task SetEmailAsync(Guid userId, Email email, bool confirmed, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(email);

        var user = await RequireUserAsync(userId, cancellationToken);
        user.Email = email.Value;
        user.EmailConfirmed = confirmed;

        await UpdateUniqueValueAsync(user, "set the email");
    }

    public async Task SetPhoneAsync(Guid userId, PhoneNumber phone, bool confirmed, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(phone);

        var user = await RequireUserAsync(userId, cancellationToken);
        user.PhoneNumber = phone.Value;
        user.PhoneNumberConfirmed = confirmed;

        await UpdateUniqueValueAsync(user, "set the phone number");
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

    public async Task SetDisplayNameAsync(Guid userId, string? displayName, CancellationToken cancellationToken)
    {
        var user = await RequireUserAsync(userId, cancellationToken);
        user.DisplayName = ApplicationUserMapper.TrimDisplayName(displayName);

        (await userManager.UpdateAsync(user)).EnsureSucceeded("update the display name");
    }

    public async Task SetActiveAsync(Guid userId, bool isActive, CancellationToken cancellationToken)
    {
        var user = await RequireUserAsync(userId, cancellationToken);
        user.IsActive = isActive;

        (await userManager.UpdateAsync(user)).EnsureSucceeded("update the account status");
    }

    public async Task RemovePhoneAsync(Guid userId, CancellationToken cancellationToken)
    {
        var user = await RequireUserAsync(userId, cancellationToken);
        user.PhoneNumber = null;
        user.PhoneNumberConfirmed = false;

        (await userManager.UpdateAsync(user)).EnsureSucceeded("remove the phone number");
    }

    // El interceptor convierte el Delete de Identity en borrado lógico y completa la auditoría.
    public async Task DeleteAsync(Guid userId, CancellationToken cancellationToken) =>
        (await userManager.DeleteAsync(await RequireUserAsync(userId, cancellationToken)))
            .EnsureSucceeded("delete the user");

    public async Task UpdateProfileAsync(
        Guid userId, string? displayName, string culture, string timeZoneId, CancellationToken cancellationToken)
    {
        var user = await RequireUserAsync(userId, cancellationToken);
        user.DisplayName = ApplicationUserMapper.TrimDisplayName(displayName);
        user.Culture = culture;
        user.TimeZoneId = timeZoneId;

        (await userManager.UpdateAsync(user)).EnsureSucceeded("update the profile");
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
            DisplayName = ApplicationUserMapper.TrimDisplayName(displayName),
            Culture = culture,
        };

        // El nombre de usuario es el Id, así cambiar correo o teléfono no altera la identidad de la cuenta.
        user.UserName = user.Id.ToString("D", CultureInfo.InvariantCulture);
        return user;
    }

    private async Task<UserAccount> CreateUserAsync(ApplicationUser user, Email? email)
    {
        (await userManager.CreateAsync(user)).EnsureSucceeded("create the user");
        (await userManager.AddToRoleAsync(
            user, email is not null && initialAdmin.IsInitialAdmin(email) ? SystemRoles.Admin : SystemRoles.User))
            .EnsureSucceeded("assign the initial role");

        return ApplicationUserMapper.ToAccount(user);
    }

    /// <summary>
    /// Un índice único puede chocar después de la lectura previa. EF revierte solo este SaveChanges y el tracker
    /// recupera lo persistido, para que el caso de uso pueda guardar otras marcas (por ejemplo, el código gastado).
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

    private async Task<ApplicationUser> RequireUserAsync(Guid userId, CancellationToken cancellationToken) =>
        await userManager.Users.FirstOrDefaultAsync(user => user.Id == userId, cancellationToken)
            ?? throw new InvalidOperationException("The user does not exist.");
}
