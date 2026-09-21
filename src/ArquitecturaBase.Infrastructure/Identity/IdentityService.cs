using System.Linq.Expressions;
using System.Security.Claims;
using ArquitecturaBase.Application.Abstractions.Identity;
using ArquitecturaBase.Application.Common.Pagination;
using ArquitecturaBase.Application.Features.Users.GetUsers;
using ArquitecturaBase.Domain.Authorization;
using ArquitecturaBase.Domain.ValueObjects;
using ArquitecturaBase.Infrastructure.Persistence.Extensions;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace ArquitecturaBase.Infrastructure.Identity;

internal sealed class IdentityService(
    UserManager<ApplicationUser> userManager,
    SignInManager<ApplicationUser> signInManager,
    IOptions<SeedOptions> seedOptions)
    : IIdentityService
{
    private const string LikeEscapeCharacter = "\\";

    // Lista blanca: los mismos nombres que GetUsersQuery.SortableFields.
    private static readonly Dictionary<string, Expression<Func<ApplicationUser, object?>>> SortMap = new()
    {
        ["email"] = user => user.Email,
        ["displayName"] = user => user.DisplayName,
        ["createdAtUtc"] = user => user.CreatedAtUtc,
    };

    private static readonly SortDescriptor DefaultSort = new("createdAtUtc", Descending: true);

    public async Task<UserAccount?> FindByIdAsync(Guid userId, CancellationToken cancellationToken) =>
        ToAccountOrNull(await FindUserAsync(userId, cancellationToken));

    public async Task<UserAccount?> FindByEmailAsync(Email email, CancellationToken cancellationToken) =>
        ToAccountOrNull(await userManager.FindByEmailAsync(email.Value));

    public async Task<UserAccount?> FindByExternalLoginAsync(string provider, string providerKey, CancellationToken cancellationToken) =>
        ToAccountOrNull(await userManager.FindByLoginAsync(provider, providerKey));

    public async Task<UserAccount> CreateAsync(Email email, string? displayName, string culture, CancellationToken cancellationToken)
    {
        var user = new ApplicationUser
        {
            UserName = email.Value,
            Email = email.Value,
            EmailConfirmed = true,
            DisplayName = displayName is { Length: > ApplicationUser.DisplayNameMaxLength }
                ? displayName[..ApplicationUser.DisplayNameMaxLength]
                : displayName,
            Culture = culture,
        };

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

    public async Task<IReadOnlyCollection<string>> GetRolesAsync(Guid userId, CancellationToken cancellationToken)
    {
        var roles = await userManager.GetRolesAsync(await RequireUserAsync(userId, cancellationToken));

        return [.. roles.Order(StringComparer.Ordinal)];
    }

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

    public Task<PagedResult<UserListItem>> ListUsersAsync(PagedRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var users = userManager.Users.AsNoTracking();

        if (!string.IsNullOrWhiteSpace(request.Search))
        {
            // "%" y "_" del texto buscado son literales, no comodines.
            var pattern = "%" + EscapeLike(request.Search.Trim()) + "%";
            users = users.Where(user =>
                EF.Functions.ILike(user.Email!, pattern, LikeEscapeCharacter)
                || (user.DisplayName != null && EF.Functions.ILike(user.DisplayName, pattern, LikeEscapeCharacter)));
        }

        return users
            .ApplySort(SortDescriptor.Parse(request.Sort), SortMap, DefaultSort, user => user.Id)
            .Select(user => new UserListItem(user.Id, user.Email!, user.DisplayName, user.IsActive, user.CreatedAtUtc))
            .ToPagedResultAsync(request, cancellationToken);
    }

    private bool IsAdminEmail(Email email)
    {
        var adminEmail = Email.Create(seedOptions.Value.AdminEmail);

        return adminEmail.IsSuccess && adminEmail.Value.Equals(email);
    }

    private Task<ApplicationUser?> FindUserAsync(Guid userId, CancellationToken cancellationToken) =>
        userManager.Users.FirstOrDefaultAsync(user => user.Id == userId, cancellationToken);

    private async Task<ApplicationUser> RequireUserAsync(Guid userId, CancellationToken cancellationToken) =>
        await FindUserAsync(userId, cancellationToken) ?? throw new InvalidOperationException("The user does not exist.");

    private static string EscapeLike(string value) =>
        value
            .Replace(LikeEscapeCharacter, LikeEscapeCharacter + LikeEscapeCharacter, StringComparison.Ordinal)
            .Replace("%", LikeEscapeCharacter + "%", StringComparison.Ordinal)
            .Replace("_", LikeEscapeCharacter + "_", StringComparison.Ordinal);

    private static UserAccount? ToAccountOrNull(ApplicationUser? user) => user is null ? null : ToAccount(user);

    private static UserAccount ToAccount(ApplicationUser user) =>
        new(user.Id, user.Email!, user.DisplayName, user.Culture, user.TimeZoneId, user.IsActive);
}
