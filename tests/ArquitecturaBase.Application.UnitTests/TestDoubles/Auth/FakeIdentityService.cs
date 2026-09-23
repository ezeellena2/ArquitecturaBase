using ArquitecturaBase.Application.Abstractions.Identity;
using ArquitecturaBase.Application.Common.Pagination;
using ArquitecturaBase.Domain.Authorization;
using ArquitecturaBase.Application.Features.Roles.GetRoles;
using ArquitecturaBase.Application.Features.Users.GetUser;
using ArquitecturaBase.Application.Features.Users.GetUsers;
using ArquitecturaBase.Domain.ValueObjects;

namespace ArquitecturaBase.Application.UnitTests.TestDoubles.Auth;

/// <summary>IIdentityService en memoria. Registra lo que hicieron los casos de uso para poder verificarlo.</summary>
internal sealed class FakeIdentityService : IIdentityService
{
    public const string DefaultTimeZoneId = "America/Argentina/Buenos_Aires";

    private readonly List<UserAccount> _users = [];
    private readonly Dictionary<(string Provider, string Key), Guid> _externalLogins = [];
    private readonly Dictionary<Guid, string[]> _roles = [];

    public IReadOnlyList<UserAccount> Users => _users;

    public Dictionary<Guid, int> FailedAttempts { get; } = [];

    public HashSet<Guid> LockedOutUsers { get; } = [];

    public List<Guid> SignedInUsers { get; } = [];

    public ExternalLogin? PendingExternalLogin { get; set; }

    public bool ExternalSignedOut { get; private set; }

    public UserListRequest? LastListRequest { get; private set; }

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

    public Task<UserAccount> CreateAsync(
        Email? email,
        PhoneNumber? phone,
        bool phoneConfirmed,
        string? displayName,
        string culture,
        CancellationToken cancellationToken)
    {
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
            displayName,
            culture,
            DefaultTimeZoneId,
            IsActive: true);
        _users.Add(user);
        _roles[user.Id] = ["User"];

        return Task.FromResult(user);
    }

    public Task AddExternalLoginAsync(Guid userId, ExternalLogin login, CancellationToken cancellationToken)
    {
        LinkExternalLogin(userId, login.Provider, login.ProviderKey);

        return Task.CompletedTask;
    }

    public Task<bool> HasExternalLoginAsync(Guid userId, string provider, CancellationToken cancellationToken) =>
        Task.FromResult(_externalLogins.Any(login => login.Key.Provider == provider && login.Value == userId));

    public Task SetPhoneAsync(Guid userId, PhoneNumber phone, bool confirmed, CancellationToken cancellationToken) =>
        Update(userId, user => user with { PhoneNumber = phone.Value, PhoneNumberConfirmed = confirmed });

    public Task RemovePhoneAsync(Guid userId, CancellationToken cancellationToken) =>
        Update(userId, user => user with { PhoneNumber = null, PhoneNumberConfirmed = false });

    public Task SetEmailAsync(Guid userId, Email email, bool confirmed, CancellationToken cancellationToken) =>
        Update(userId, user => user with { Email = email.Value, EmailConfirmed = confirmed });

    public Task<IReadOnlyCollection<string>> GetRolesAsync(Guid userId, CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyCollection<string>>(_roles.GetValueOrDefault(userId) ?? []);

    public Task<bool> IsLockedOutAsync(Guid userId, CancellationToken cancellationToken) =>
        Task.FromResult(LockedOutUsers.Contains(userId));

    public Task RegisterFailedAttemptAsync(Guid userId, CancellationToken cancellationToken)
    {
        FailedAttempts[userId] = FailedAttempts.GetValueOrDefault(userId) + 1;

        return Task.CompletedTask;
    }

    public Task ResetFailedAttemptsAsync(Guid userId, CancellationToken cancellationToken)
    {
        FailedAttempts[userId] = 0;

        return Task.CompletedTask;
    }

    public Task SignInAsync(Guid userId, CancellationToken cancellationToken)
    {
        SignedInUsers.Add(userId);

        return Task.CompletedTask;
    }

    public Task<ExternalLogin?> GetExternalLoginAsync(CancellationToken cancellationToken) =>
        Task.FromResult(PendingExternalLogin);

    public Task SignOutExternalAsync(CancellationToken cancellationToken)
    {
        ExternalSignedOut = true;

        return Task.CompletedTask;
    }

    public Task<PagedResult<UserListItem>> ListUsersAsync(UserListRequest request, CancellationToken cancellationToken)
    {
        LastListRequest = request;
        var items = _users.Select(user => new UserListItem(
            user.Id,
            user.Email,
            user.PhoneNumber,
            user.PhoneNumberConfirmed,
            user.DisplayName,
            user.IsActive,
            default,
            _roles.GetValueOrDefault(user.Id) ?? [])).ToList();

        return Task.FromResult(new PagedResult<UserListItem>(items, request.Page, request.PageSize, items.Count));
    }

    /// Los conteos de verdad se prueban contra la base, en integración: acá solo tiene que existir.
    public Task<UserFilterCounts> GetUserFilterCountsAsync(UserListRequest request, CancellationToken cancellationToken)
    {
        LastListRequest = request;
        var active = _users.Count(user => user.IsActive);

        return Task.FromResult(new UserFilterCounts(
            new UserStatusCounts(_users.Count, active, _users.Count - active),
            [],
            []));
    }

    /// <summary>Cuentas borradas lógicamente: las ve el alta, que las restaura. Acompaña a DeletedEmails (Tarea 6).</summary>
    public List<UserAccount> DeletedUsers { get; } = [];

    /// <summary>A quiénes se les cortó el acceso ya emitido.</summary>
    public List<Guid> RevokedUsers { get; } = [];

    /// <summary>Los roles que existen en el sistema. El alta y la edición validan contra esta lista.</summary>
    public List<string> RoleNames { get; } = ["Admin", "User"];

    /// <summary>Correos con una cuenta borrada lógicamente: el doble no las guarda en <see cref="Users"/>.</summary>
    public HashSet<string> DeletedEmails { get; } = new(StringComparer.Ordinal);

    public Task<bool> IsDeletedEmailAsync(Email email, CancellationToken cancellationToken) =>
        Task.FromResult(DeletedEmails.Contains(email.Value));

    public Task<bool> IsDeletedPhoneAsync(PhoneNumber phone, CancellationToken cancellationToken) =>
        Task.FromResult(DeletedUsers.Any(user => user.PhoneNumber == phone.Value));

    public Task<int> CountActiveAdminsAsync(CancellationToken cancellationToken) =>
        Task.FromResult(_users.Count(user =>
            user.IsActive && (_roles.GetValueOrDefault(user.Id) ?? []).Contains(SystemRoles.Admin, StringComparer.Ordinal)));

    public Task<UserAccount?> FindDeletedByEmailAsync(Email email, CancellationToken cancellationToken) =>
        Task.FromResult(DeletedUsers.SingleOrDefault(user => user.Email == email.Value));

    public Task RestoreAsync(Guid userId, string? displayName, CancellationToken cancellationToken)
    {
        var user = DeletedUsers.Single(user => user.Id == userId);
        DeletedUsers.Remove(user);

        // DeletedEmails lo dejó la Tarea 6 y lo lee IsDeletedEmailAsync: los dos tienen que decir lo mismo.
        if (user.Email is not null)
        {
            DeletedEmails.Remove(user.Email);
        }

        _users.Add(user with { DisplayName = displayName, IsActive = true });
        _roles[user.Id] = [];

        return Task.CompletedTask;
    }

    public Task SetRolesAsync(Guid userId, IReadOnlyCollection<string> roles, CancellationToken cancellationToken)
    {
        _roles[userId] = [.. roles];

        return Task.CompletedTask;
    }

    public Task<IReadOnlyCollection<string>> ListRoleNamesAsync(CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyCollection<string>>(RoleNames);

    public Task<UserDetail?> FindDetailAsync(Guid userId, CancellationToken cancellationToken)
    {
        var user = _users.SingleOrDefault(user => user.Id == userId);

        return Task.FromResult(user is null
            ? null
            : new UserDetail(
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

    public Task SetDisplayNameAsync(Guid userId, string? displayName, CancellationToken cancellationToken)
    {
        var index = _users.FindIndex(user => user.Id == userId);
        _users[index] = _users[index] with { DisplayName = displayName };

        return Task.CompletedTask;
    }

    public Task SetActiveAsync(Guid userId, bool isActive, CancellationToken cancellationToken)
    {
        var index = _users.FindIndex(user => user.Id == userId);
        _users[index] = _users[index] with { IsActive = isActive };

        return Task.CompletedTask;
    }

    public Task RevokeSessionsAsync(Guid userId, CancellationToken cancellationToken)
    {
        RevokedUsers.Add(userId);

        return Task.CompletedTask;
    }

    public Task DeleteAsync(Guid userId, CancellationToken cancellationToken)
    {
        var user = _users.Single(user => user.Id == userId);
        _users.Remove(user);
        _roles.Remove(userId);
        DeletedUsers.Add(user);

        // DeletedEmails lo lee IsDeletedEmailAsync (Tarea 6): los dos tienen que decir lo mismo.
        if (user.Email is not null)
        {
            DeletedEmails.Add(user.Email);
        }

        return Task.CompletedTask;
    }

    public Task<IReadOnlyCollection<RoleListItem>> ListRolesAsync(CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyCollection<RoleListItem>>(
        [
            .. RoleNames.Order(StringComparer.Ordinal).Select(name => new RoleListItem(
                Guid.CreateVersion7(),
                name,
                Description: null,
                SystemRoles.All.Contains(name, StringComparer.Ordinal),
                _users.Count(user => (_roles.GetValueOrDefault(user.Id) ?? []).Contains(name, StringComparer.Ordinal)),
                [])),
        ]);

    public Task<RoleListItem?> FindRoleAsync(Guid roleId, CancellationToken cancellationToken) =>
        Task.FromResult(ListRolesAsync(cancellationToken).Result.SingleOrDefault(role => role.Id == roleId));

    public Task<bool> RoleNameExistsAsync(string name, Guid? excludedRoleId, CancellationToken cancellationToken) =>
        Task.FromResult(RoleNames.Contains(name, StringComparer.OrdinalIgnoreCase));

    public Task<Guid> CreateRoleAsync(
        string name, string? description, IReadOnlyCollection<string> permissions, CancellationToken cancellationToken)
    {
        RoleNames.Add(name);

        return Task.FromResult(Guid.CreateVersion7());
    }

    public Task UpdateRoleAsync(
        Guid roleId, string name, string? description, IReadOnlyCollection<string> permissions, CancellationToken cancellationToken) =>
        Task.CompletedTask;

    public Task DeleteRoleAsync(Guid roleId, CancellationToken cancellationToken) => Task.CompletedTask;

    public Task UpdateProfileAsync(
        Guid userId, string? displayName, string culture, string timeZoneId, CancellationToken cancellationToken)
    {
        var index = _users.FindIndex(user => user.Id == userId);
        _users[index] = _users[index] with { DisplayName = displayName, Culture = culture, TimeZoneId = timeZoneId };

        return Task.CompletedTask;
    }

    private Task Update(Guid userId, Func<UserAccount, UserAccount> change)
    {
        var index = _users.FindIndex(user => user.Id == userId);
        _users[index] = change(_users[index]);

        return Task.CompletedTask;
    }
}
