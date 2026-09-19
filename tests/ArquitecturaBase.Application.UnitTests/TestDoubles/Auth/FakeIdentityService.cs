using ArquitecturaBase.Application.Abstractions.Identity;
using ArquitecturaBase.Application.Common.Pagination;
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

    public PagedRequest? LastListRequest { get; private set; }

    public UserAccount AddUser(string email, bool isActive = true, string culture = "es")
    {
        var user = new UserAccount(Guid.CreateVersion7(), email, DisplayName: null, culture, DefaultTimeZoneId, isActive);
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

    public Task<UserAccount> CreateAsync(Email email, string? displayName, string culture, CancellationToken cancellationToken)
    {
        var user = new UserAccount(Guid.CreateVersion7(), email.Value, displayName, culture, DefaultTimeZoneId, IsActive: true);
        _users.Add(user);
        _roles[user.Id] = ["User"];

        return Task.FromResult(user);
    }

    public Task AddExternalLoginAsync(Guid userId, ExternalLogin login, CancellationToken cancellationToken)
    {
        LinkExternalLogin(userId, login.Provider, login.ProviderKey);

        return Task.CompletedTask;
    }

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

    public Task<PagedResult<UserListItem>> ListUsersAsync(PagedRequest request, CancellationToken cancellationToken)
    {
        LastListRequest = request;
        var items = _users.Select(user => new UserListItem(user.Id, user.Email, user.DisplayName, user.IsActive, default)).ToList();

        return Task.FromResult(new PagedResult<UserListItem>(items, request.Page, request.PageSize, items.Count));
    }
}
