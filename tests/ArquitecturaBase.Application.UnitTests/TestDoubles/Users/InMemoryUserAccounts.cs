using ArquitecturaBase.Application.Common.Pagination;
using ArquitecturaBase.Application.Interfaces.Persistence;
using ArquitecturaBase.Application.Models.Identity;
using ArquitecturaBase.Application.Models.Users;
using ArquitecturaBase.Domain.Authorization;
using ArquitecturaBase.Domain.Users;
using ArquitecturaBase.Domain.ValueObjects;

namespace ArquitecturaBase.Application.UnitTests.TestDoubles.Users;

/// <summary>
/// Las cuentas en memoria: IUserReader e IUserRepository sobre la misma lista, como en producción los dos van sobre la
/// misma base. Registra lo que hicieron los casos de uso para poder verificarlo. El bloqueo, la cookie y el cierre de
/// sesiones están en FakeSignInService.
/// </summary>
internal sealed class InMemoryUserAccounts : IUserReader, IUserRepository
{
    public const string DefaultTimeZoneId = "America/Argentina/Buenos_Aires";

    private readonly List<UserAccount> _users = [];
    private readonly Dictionary<(string Provider, string Key), Guid> _externalLogins = [];
    private readonly Dictionary<Guid, string[]> _roles = [];

    public IReadOnlyList<UserAccount> Users => _users;

    public ListUsersRequest? LastListRequest { get; private set; }

    /// <summary>Cuentas borradas lógicamente: las ve el alta, que las restaura. Acompaña a <see cref="DeletedEmails"/>.</summary>
    public List<UserAccount> DeletedUsers { get; } = [];

    /// <summary>Correos con una cuenta borrada lógicamente: el doble no las guarda en <see cref="Users"/>.</summary>
    public HashSet<string> DeletedEmails { get; } = new(StringComparer.Ordinal);

    /// <summary>
    /// Si no es null, tomar el lock o escribir fuera de la transacción lanza, como en producción (ver
    /// <see cref="TransactionGuard"/>). Lo que el test arma con <see cref="AddUser"/>, <see cref="SetRoles"/> y
    /// <see cref="LinkExternalLogin"/> no pasa por la guarda.
    /// </summary>
    public Func<bool>? InTransaction { get; set; }

    /// <summary>
    /// El lock de administradores y los conteos, en orden: "lock-admins" y "count-admins". Así un test ve si el lock se
    /// tomó, y si se tomó antes de contar.
    /// </summary>
    public List<string> AdminEvents { get; } = [];

    /// <summary>Corre al tomar el lock de administradores: el test mira qué otros locks ya estaban tomados.</summary>
    public Action? OnLockAdmins { get; set; }

    /// <summary>Una cuenta con el correo verificado, o solo con el número (también verificado) si no hay correo.</summary>
    public UserAccount AddUser(string? email, bool isActive = true, string culture = "es", string? phoneNumber = null)
    {
        var user = new UserAccount(
            Guid.CreateVersion7(),
            email,
            EmailConfirmed: email is not null,
            phoneNumber,
            PhoneNumberConfirmed: phoneNumber is not null,
            DisplayName: null,
            culture,
            DefaultTimeZoneId,
            isActive);
        _users.Add(user);
        _roles[user.Id] = [];

        return user;
    }

    public void SetRoles(Guid userId, params string[] roles) => _roles[userId] = roles;

    public void LinkExternalLogin(Guid userId, string provider, string providerKey) =>
        _externalLogins[(provider, providerKey)] = userId;

    /// <summary>
    /// Arma datos con las escrituras del doble sin pasar por la guarda de <see cref="InTransaction"/>, como
    /// ApiFactory.InTransactionAsync en integración: lo que se prepara antes del caso de uso no es parte de su límite.
    /// </summary>
    public async Task<T> ArrangeAsync<T>(Func<InMemoryUserAccounts, Task<T>> arrange)
    {
        ArgumentNullException.ThrowIfNull(arrange);

        var guard = InTransaction;
        InTransaction = null;

        try
        {
            return await arrange(this);
        }
        finally
        {
            InTransaction = guard;
        }
    }

    /// <inheritdoc cref="ArrangeAsync{T}(Func{InMemoryUserAccounts, Task{T}})"/>
    public Task ArrangeAsync(Func<InMemoryUserAccounts, Task> arrange)
    {
        ArgumentNullException.ThrowIfNull(arrange);

        return ArrangeAsync(async accounts =>
        {
            await arrange(accounts);

            return true;
        });
    }

    public Task<UserAccount?> FindByIdAsync(Guid userId, CancellationToken cancellationToken) =>
        Task.FromResult(_users.SingleOrDefault(user => user.Id == userId));

    public Task<UserAccount?> FindByEmailAsync(Email email, CancellationToken cancellationToken) =>
        Task.FromResult(_users.SingleOrDefault(user => user.Email == email.Value));

    public Task<UserAccount?> FindByExternalLoginAsync(string provider, string providerKey, CancellationToken cancellationToken) =>
        Task.FromResult(_externalLogins.TryGetValue((provider, providerKey), out var userId)
            ? _users.Single(user => user.Id == userId)
            : null);

    public Task<UserAccount?> FindByPhoneAsync(PhoneNumber phone, CancellationToken cancellationToken) =>
        Task.FromResult(_users.SingleOrDefault(user => user.PhoneNumber == phone.Value));

    public Task<bool> ExistsDeletedByEmailAsync(Email email, CancellationToken cancellationToken) =>
        Task.FromResult(DeletedEmails.Contains(email.Value));

    public Task<bool> ExistsDeletedByPhoneAsync(PhoneNumber phone, CancellationToken cancellationToken) =>
        Task.FromResult(DeletedUsers.Any(user => user.PhoneNumber == phone.Value));

    public Task<UserAccount?> FindDeletedByEmailAsync(Email email, CancellationToken cancellationToken) =>
        Task.FromResult(DeletedUsers.SingleOrDefault(user => user.Email == email.Value));

    public Task<UserAccount?> FindDeletedByPhoneAsync(PhoneNumber phone, CancellationToken cancellationToken) =>
        Task.FromResult(DeletedUsers.SingleOrDefault(user => user.PhoneNumber == phone.Value));

    public Task<bool> ExistsExternalLoginAsync(Guid userId, string provider, CancellationToken cancellationToken) =>
        Task.FromResult(_externalLogins.Any(login => login.Key.Provider == provider && login.Value == userId));

    public Task<IReadOnlyCollection<string>> ListRoleNamesForUserAsync(Guid userId, CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyCollection<string>>(_roles.GetValueOrDefault(userId) ?? []);

    public Task<UserDetailRow?> FindDetailAsync(Guid userId, CancellationToken cancellationToken)
    {
        var user = _users.SingleOrDefault(user => user.Id == userId);

        return Task.FromResult(user is null
            ? null
            : new UserDetailRow(
                user.Id,
                user.Email,
                user.EmailConfirmed,
                user.PhoneNumber,
                user.PhoneNumberConfirmed,
                user.DisplayName,
                user.IsActive,
                default,
                [.. (_roles.GetValueOrDefault(user.Id) ?? []).Order(StringComparer.Ordinal)]));
    }

    public Task<int> CountActiveAdminsAsync(CancellationToken cancellationToken)
    {
        AdminEvents.Add("count-admins");

        return Task.FromResult(_users.Count(user =>
            user.IsActive && (_roles.GetValueOrDefault(user.Id) ?? []).Contains(SystemRoles.Admin, StringComparer.Ordinal)));
    }

    public Task<PagedResult<UserListRow>> ListUsersAsync(ListUsersRequest request, CancellationToken cancellationToken)
    {
        LastListRequest = request;
        var items = _users.Select(user => new UserListRow(
            user.Id,
            user.Email,
            user.PhoneNumber,
            user.PhoneNumberConfirmed,
            user.DisplayName,
            user.IsActive,
            default,
            _roles.GetValueOrDefault(user.Id) ?? [])).ToList();

        return Task.FromResult(new PagedResult<UserListRow>(items, request.Page, request.PageSize, items.Count));
    }

    /// <summary>Los conteos de verdad se prueban contra la base, en integración: acá solo tiene que existir.</summary>
    public Task<UserFilterCounts> CountByFilterOptionAsync(ListUsersRequest request, CancellationToken cancellationToken)
    {
        LastListRequest = request;
        var active = _users.Count(user => user.IsActive);

        return Task.FromResult(new UserFilterCounts(
            new UserStatusCounts(_users.Count, active, _users.Count - active),
            [],
            []));
    }

    public Task LockAdminsAsync(CancellationToken cancellationToken)
    {
        TransactionGuard.Require(InTransaction);
        OnLockAdmins?.Invoke();
        AdminEvents.Add("lock-admins");

        return Task.CompletedTask;
    }

    public Task LockExternalSignInAsync(
        Email email, string provider, string providerKey, CancellationToken cancellationToken)
    {
        TransactionGuard.Require(InTransaction);

        return Task.CompletedTask;
    }

    public Task<UserAccount> CreateAsync(
        Email? email,
        PhoneNumber? phone,
        bool phoneConfirmed,
        string? displayName,
        string culture,
        CancellationToken cancellationToken,
        string timeZoneId = ArquitecturaBase.Domain.Settings.SystemSettings.InitialTimeZoneId)
    {
        TransactionGuard.Require(InTransaction);

        if (email is null && phone is null)
        {
            throw new ArgumentException("An account needs an email or a phone number.", nameof(email));
        }

        var user = new UserAccount(
            Guid.CreateVersion7(),
            email?.Value,
            EmailConfirmed: email is not null,
            phone?.Value,
            PhoneNumberConfirmed: phone is not null && phoneConfirmed,
            Named(displayName),
            culture,
            timeZoneId,
            IsActive: true);
        _users.Add(user);
        _roles[user.Id] = ["User"];

        return Task.FromResult(user);
    }

    /// <summary>El alta de un administrador: el correo y el número quedan sin verificar.</summary>
    public Task<UserAccount> CreateUnverifiedAsync(
        Email? email,
        PhoneNumber? phone,
        string? displayName,
        string culture,
        CancellationToken cancellationToken,
        string timeZoneId = ArquitecturaBase.Domain.Settings.SystemSettings.InitialTimeZoneId)
    {
        TransactionGuard.Require(InTransaction);

        if (email is null && phone is null)
        {
            throw new ArgumentException("An account needs an email or a phone number.", nameof(email));
        }

        var user = new UserAccount(
            Guid.CreateVersion7(),
            email?.Value,
            EmailConfirmed: false,
            phone?.Value,
            PhoneNumberConfirmed: false,
            Named(displayName),
            culture,
            timeZoneId,
            IsActive: true);
        _users.Add(user);
        _roles[user.Id] = ["User"];

        return Task.FromResult(user);
    }

    public Task AddExternalLoginAsync(Guid userId, ExternalLogin login, CancellationToken cancellationToken)
    {
        TransactionGuard.Require(InTransaction);
        LinkExternalLogin(userId, login.Provider, login.ProviderKey);

        return Task.CompletedTask;
    }

    public Task RestoreAsync(Guid userId, string? displayName, CancellationToken cancellationToken)
    {
        TransactionGuard.Require(InTransaction);
        var user = DeletedUsers.Single(user => user.Id == userId);
        DeletedUsers.Remove(user);

        // ExistsDeletedByEmailAsync lee DeletedEmails: los dos tienen que decir lo mismo.
        if (user.Email is not null)
        {
            DeletedEmails.Remove(user.Email);
        }

        _users.Add(user with { DisplayName = Named(displayName), IsActive = true });
        _roles[user.Id] = [];

        return Task.CompletedTask;
    }

    public Task SetEmailAsync(Guid userId, Email email, bool confirmed, CancellationToken cancellationToken) =>
        Update(userId, user => user with { Email = email.Value, EmailConfirmed = confirmed });

    public Task SetPhoneAsync(Guid userId, PhoneNumber phone, bool confirmed, CancellationToken cancellationToken) =>
        Update(userId, user => user with { PhoneNumber = phone.Value, PhoneNumberConfirmed = confirmed });

    public Task RemovePhoneAsync(Guid userId, CancellationToken cancellationToken) =>
        Update(userId, user => user with { PhoneNumber = null, PhoneNumberConfirmed = false });

    public Task SetRolesAsync(Guid userId, IReadOnlyCollection<string> roles, CancellationToken cancellationToken)
    {
        TransactionGuard.Require(InTransaction);
        _roles[userId] = [.. roles];

        return Task.CompletedTask;
    }

    public Task SetDisplayNameAsync(Guid userId, string? displayName, CancellationToken cancellationToken) =>
        Update(userId, user => user with { DisplayName = Named(displayName) });

    public Task SetActiveAsync(Guid userId, bool isActive, CancellationToken cancellationToken) =>
        Update(userId, user => user with { IsActive = isActive });

    public Task DeleteAsync(Guid userId, CancellationToken cancellationToken)
    {
        TransactionGuard.Require(InTransaction);
        var user = _users.Single(user => user.Id == userId);
        _users.Remove(user);
        _roles.Remove(userId);
        DeletedUsers.Add(user);

        // ExistsDeletedByEmailAsync lee DeletedEmails: los dos tienen que decir lo mismo.
        if (user.Email is not null)
        {
            DeletedEmails.Add(user.Email);
        }

        return Task.CompletedTask;
    }

    public Task UpdateProfileAsync(
        Guid userId, string? displayName, string culture, string timeZoneId, CancellationToken cancellationToken) =>
        Update(userId, user => user with { DisplayName = Named(displayName), Culture = culture, TimeZoneId = timeZoneId });

    // Como ApplicationUser.Rename: un nombre más largo que la columna es un bug de quien llama (HTTP lo valida y los
    // nombres de afuera se recortan en la entrada), así que el doble también lanza.
    private static string? Named(string? displayName) =>
        AccountRules.IsValidDisplayName(displayName)
            ? displayName
            : throw new ArgumentException(
                $"The display name exceeds {AccountRules.DisplayNameMaxLength} characters.", nameof(displayName));

    private Task Update(Guid userId, Func<UserAccount, UserAccount> change)
    {
        TransactionGuard.Require(InTransaction);
        var index = _users.FindIndex(user => user.Id == userId);
        _users[index] = change(_users[index]);

        return Task.CompletedTask;
    }
}
