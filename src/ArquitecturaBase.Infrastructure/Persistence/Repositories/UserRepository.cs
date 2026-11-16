using ArquitecturaBase.Application.Interfaces.Integrations.Identity;
using ArquitecturaBase.Application.Interfaces.Persistence;
using ArquitecturaBase.Application.Models.Identity;
using ArquitecturaBase.Domain.Authentication;
using ArquitecturaBase.Domain.Authorization;
using ArquitecturaBase.Domain.Users;
using ArquitecturaBase.Domain.ValueObjects;
using ArquitecturaBase.Infrastructure.Identity;
using ArquitecturaBase.Infrastructure.Persistence.Extensions;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace ArquitecturaBase.Infrastructure.Persistence.Repositories;

/// <summary>
/// Escrituras de cuentas con UserManager, que guarda en cada operación sobre el contexto compartido. No abre
/// transacción: todas las escrituras, igual que LockExternalSignInAsync, exigen la del caso de uso
/// (IUnitOfWork.ExecuteInTransactionAsync) y, sin ella, lanzan InvalidOperationException antes de tocar nada. Adentro,
/// EF le pone un savepoint a cada guardado. Sin esa exigencia, una escritura fuera de un límite se confirmaría sola, y
/// una operación con varios guardados (el alta y su rol, los roles que se sacan y los que se agregan) podría quedar a
/// medias.
/// </summary>
internal sealed class UserRepository(
    UserManager<ApplicationUser> userManager,
    ApplicationDbContext dbContext,
    IInitialAdmin initialAdmin,
    IAdministrativeChangeRepository changes,
    TimeProvider timeProvider) : IUserRepository
{
    public Task LockExternalSignInAsync(
        Email email, string provider, string providerKey, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(email);

        // En una sola llamada, que las ordena: external-login: se toma antes que login-code:. Nadie más toma
        // external-login:, así que ese orden no se cruza con otro caso de uso. login-code: es la misma clave del correo
        // que toman los códigos, el alta del administrador y el perfil.
        return dbContext.AcquireAdvisoryLocksAsync(
            [AdvisoryLockKeys.LoginCode(LoginCodeDestination.ForEmail(email)),
             AdvisoryLockKeys.ExternalLogin(provider, providerKey)],
            cancellationToken);
    }

    // Global y solo: lo toma UserGuard justo antes de contar los administradores activos, después de los locks de los
    // participantes del número y de cuenta del caso de uso, y nadie toma otro lock después. Así no hay ciclo con ninguno.
    public Task LockAdminsAsync(CancellationToken cancellationToken) =>
        dbContext.AcquireAdvisoryLocksAsync([AdvisoryLockKeys.Admins], cancellationToken);

    // FOR UPDATE y no FOR NO KEY UPDATE: cambiar el correo o el número toca columnas con índice único, y ese UPDATE pide
    // FOR UPDATE, que choca con el FOR KEY SHARE que toma quien inserta una fila que apunta a la cuenta (un ingreso, un
    // código). Si la edición tomara antes FOR NO KEY UPDATE, que no choca con ese lock, quedaría esperando al ingreso
    // mientras el ingreso, al escribir la cuenta, la espera a ella: un deadlock (40P01). Tomando FOR UPDATE de entrada,
    // la edición espera a que termine el ingreso, o el ingreso a que termine la edición. También la fila de una cuenta
    // borrada: el filtro global no cuenta en SQL crudo, y quien llama decide con la lectura que sigue.
    public async Task LockAsync(Guid userId, CancellationToken cancellationToken)
    {
        dbContext.RequireTransaction();

        await dbContext.Database.ExecuteSqlInterpolatedAsync(
            $"SELECT 1 FROM \"AspNetUsers\" WHERE \"Id\" = {userId} FOR UPDATE", cancellationToken);
    }

    public async Task<UserAccount> CreateAsync(
        Email? email,
        PhoneNumber? phone,
        bool phoneConfirmed,
        string? displayName,
        string culture,
        CancellationToken cancellationToken,
        string timeZoneId = ArquitecturaBase.Domain.Settings.SystemSettings.InitialTimeZoneId)
    {
        dbContext.RequireTransaction();

        return await CreateUserAsync(
            NewUser(email, emailConfirmed: email is not null, phone, phoneConfirmed, displayName, culture, timeZoneId), email,
            signingIn: true);
    }

    public async Task<UserAccount> CreateUnverifiedAsync(
        Email? email,
        PhoneNumber? phone,
        string? displayName,
        string culture,
        CancellationToken cancellationToken,
        string timeZoneId = ArquitecturaBase.Domain.Settings.SystemSettings.InitialTimeZoneId)
    {
        dbContext.RequireTransaction();

        var user = NewUser(email, emailConfirmed: false, phone, phoneConfirmed: false, displayName, culture, timeZoneId);

        // El bot crea cuentas sin el lock del destino. Si ganó la carrera, el índice único choca acá: EF revierte solo
        // este guardado (savepoint en la transacción del caso de uso) y se despega la entidad para no reintentarla.
        try
        {
            return await CreateUserAsync(user, email, signingIn: false);
        }
        catch (DbUpdateException exception) when (UniqueViolations.Translate(exception) is { } unique)
        {
            dbContext.Entry(user).State = EntityState.Detached;
            throw unique;
        }
    }

    public async Task AddExternalLoginAsync(Guid userId, ExternalLogin login, CancellationToken cancellationToken)
    {
        dbContext.RequireTransaction();
        ArgumentNullException.ThrowIfNull(login);
        var user = await userManager.RequireUserAsync(userId, cancellationToken);

        (await userManager.AddLoginAsync(user, new UserLoginInfo(login.Provider, login.ProviderKey, login.Provider)))
            .EnsureSucceeded("link the external login");
    }

    public async Task RestoreAsync(
        Guid userId, string? displayName, string culture, string timeZoneId, CancellationToken cancellationToken)
    {
        dbContext.RequireTransaction();

        var user = await userManager.Users
            .IgnoreQueryFilters([ModelBuilderExtensions.SoftDeleteFilter])
            .FirstOrDefaultAsync(user => user.Id == userId, cancellationToken)
            ?? throw new InvalidOperationException("The user does not exist.");

        user.Restore(displayName, culture, timeZoneId);
        user.AdvanceRevision();

        (await userManager.UpdateAsync(user)).EnsureSucceeded("restore the user");

        // Los vínculos pueden ser del dueño anterior (N4): el alta nunca trae uno. Quitar uno renueva el security stamp,
        // y está bien: nada de antes tiene que seguir valiendo.
        foreach (var login in await userManager.GetLoginsAsync(user))
        {
            (await userManager.RemoveLoginAsync(user, login.LoginProvider, login.ProviderKey))
                .EnsureSucceeded("remove the external login");
        }
    }

    // No usamos SetEmailAsync/SetPhoneNumberAsync de UserManager: renuevan el security stamp. Quien necesita cortar
    // sesiones invoca la revocación aparte, después de estas escrituras.
    public async Task SetEmailAsync(Guid userId, Email email, bool confirmed, CancellationToken cancellationToken)
    {
        dbContext.RequireTransaction();
        ArgumentNullException.ThrowIfNull(email);

        var user = await userManager.RequireUserAsync(userId, cancellationToken);
        AdvanceRevisionIfChanged(user, user.Email, email.Value);
        user.Email = email.Value;
        user.EmailConfirmed = confirmed;

        await UpdateUniqueValueAsync(user, "set the email");
    }

    public async Task SetPhoneAsync(Guid userId, PhoneNumber phone, bool confirmed, CancellationToken cancellationToken)
    {
        dbContext.RequireTransaction();
        ArgumentNullException.ThrowIfNull(phone);

        var user = await userManager.RequireUserAsync(userId, cancellationToken);
        AdvanceRevisionIfChanged(user, user.PhoneNumber, phone.Value);
        user.PhoneNumber = phone.Value;
        user.PhoneNumberConfirmed = confirmed;

        await UpdateUniqueValueAsync(user, "set the phone number");
    }

    public async Task RemoveEmailAsync(Guid userId, CancellationToken cancellationToken)
    {
        dbContext.RequireTransaction();

        var user = await userManager.RequireUserAsync(userId, cancellationToken);
        AdvanceRevisionIfChanged(user, user.Email, null);
        user.Email = null;
        user.EmailConfirmed = false;

        (await userManager.UpdateAsync(user)).EnsureSucceeded("remove the email");
    }

    public async Task RemovePhoneAsync(Guid userId, CancellationToken cancellationToken)
    {
        dbContext.RequireTransaction();

        var user = await userManager.RequireUserAsync(userId, cancellationToken);
        AdvanceRevisionIfChanged(user, user.PhoneNumber, null);
        user.PhoneNumber = null;
        user.PhoneNumberConfirmed = false;

        (await userManager.UpdateAsync(user)).EnsureSucceeded("remove the phone number");
    }

    public async Task<RoleAssignment> SetRolesAsync(
        Guid userId, IReadOnlyCollection<string> roles, CancellationToken cancellationToken)
    {
        dbContext.RequireTransaction();
        ArgumentNullException.ThrowIfNull(roles);

        var user = await userManager.RequireUserAsync(userId, cancellationToken);
        var current = await userManager.GetRolesAsync(user);
        var removed = current.Except(roles, StringComparer.Ordinal).ToList();
        var added = roles.Except(current, StringComparer.Ordinal).ToList();

        // Identity la guarda con los roles: RemoveFromRolesAsync y AddToRolesAsync actualizan la cuenta.
        if (removed.Count > 0 || added.Count > 0)
        {
            user.AdvanceRevision();
        }

        if (removed.Count > 0)
        {
            (await userManager.RemoveFromRolesAsync(user, removed)).EnsureSucceeded("remove the roles");
        }

        if (added.Count > 0)
        {
            (await userManager.AddToRolesAsync(user, added)).EnsureSucceeded("assign the roles");
        }

        return new RoleAssignment(added, removed);
    }

    public async Task SetDisplayNameAsync(Guid userId, string? displayName, CancellationToken cancellationToken)
    {
        dbContext.RequireTransaction();

        var user = await userManager.RequireUserAsync(userId, cancellationToken);
        AdvanceRevisionIfChanged(user, user.DisplayName, displayName);
        user.Rename(displayName);

        (await userManager.UpdateAsync(user)).EnsureSucceeded("update the display name");
    }

    public async Task SetActiveAsync(Guid userId, bool isActive, CancellationToken cancellationToken)
    {
        dbContext.RequireTransaction();

        var user = await userManager.RequireUserAsync(userId, cancellationToken);
        if (user.IsActive != isActive)
        {
            user.AdvanceRevision();
        }

        user.SetActive(isActive);

        (await userManager.UpdateAsync(user)).EnsureSucceeded("update the account status");
    }

    // El interceptor convierte el Delete de Identity en borrado lógico y completa la auditoría.
    public async Task DeleteAsync(Guid userId, CancellationToken cancellationToken)
    {
        dbContext.RequireTransaction();

        // La fila queda: un editor abierto sobre ella choca en vez de guardar sobre una cuenta borrada y restaurada.
        var user = await userManager.RequireUserAsync(userId, cancellationToken);
        user.AdvanceRevision();

        (await userManager.DeleteAsync(user)).EnsureSucceeded("delete the user");
    }

    public async Task UpdateProfileAsync(
        Guid userId, string? displayName, string culture, string timeZoneId, CancellationToken cancellationToken)
    {
        dbContext.RequireTransaction();

        var user = await userManager.RequireUserAsync(userId, cancellationToken);

        // El idioma y la zona horaria son sólo del perfil: no avanzan la revisión que mira la administración.
        AdvanceRevisionIfChanged(user, user.DisplayName, displayName);
        user.Rename(displayName);
        user.UpdatePreferences(culture, timeZoneId);

        (await userManager.UpdateAsync(user)).EnsureSucceeded("update the profile");
    }

    // Un dato que se escribe igual (o sólo se confirma) no es un cambio: daría conflictos falsos.
    private static void AdvanceRevisionIfChanged(ApplicationUser user, string? current, string? next)
    {
        if (!string.Equals(current, next, StringComparison.Ordinal))
        {
            user.AdvanceRevision();
        }
    }

    private static ApplicationUser NewUser(
        Email? email,
        bool emailConfirmed,
        PhoneNumber? phone,
        bool phoneConfirmed,
        string? displayName,
        string culture, string timeZoneId) =>
        ApplicationUser.Create(email?.Value, emailConfirmed, phone?.Value, phoneConfirmed, displayName, culture, timeZoneId);

    // La cuenta de Seed:AdminEmail que nace en su primer ingreso (signingIn) recibe Admin por la configuración, como en el
    // seed: deja su historia sin actor (N13). El alta de un administrador (CreateUnverifiedAsync) deja la suya, con los
    // roles que pidió, en UserAdministrationService.
    private async Task<UserAccount> CreateUserAsync(ApplicationUser user, Email? email, bool signingIn)
    {
        var isInitialAdmin = email is not null && initialAdmin.IsInitialAdmin(email);
        (await userManager.CreateAsync(user)).EnsureSucceeded("create the user");
        (await userManager.AddToRoleAsync(user, isInitialAdmin ? SystemRoles.Admin : SystemRoles.User))
            .EnsureSucceeded("assign the initial role");

        if (isInitialAdmin && signingIn)
        {
            changes.Add(AdministrativeChange.Create(
                AdministrativeChangeKind.UserRolesChanged, actorUserId: null, user.Id, timeProvider.GetUtcNow().UtcDateTime,
                added: [SystemRoles.Admin]));
        }

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
}
