using ArquitecturaBase.Application.Interfaces.Persistence;
using ArquitecturaBase.Domain.Authentication;
using ArquitecturaBase.Domain.Authorization;
using ArquitecturaBase.Domain.Users;

namespace ArquitecturaBase.Application.UnitTests.Services.Users;

public sealed class UserAccessServiceStatusTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Deactivate_locks_account_revokes_sessions_and_commits_once()
    {
        var host = new UserServiceTestHost();
        var user = host.Accounts.AddUser("user@example.com");

        var result = await host.Access.SetUserActiveAsync(user.Id, isActive: false, Ct);

        Assert.True(result.IsSuccess);
        Assert.False((await host.Accounts.FindByIdAsync(user.Id, Ct))!.IsActive);
        Assert.Equal([user.Id], host.Links.LockedAccounts);
        Assert.Equal([user.Id], host.SignIn.RevokedUsers);
        Assert.Equal(1, host.UnitOfWork.Commits);
        Assert.Equal(CommitPolicy.OnSuccess, host.UnitOfWork.LastPolicy);
        Assert.Equal(["Handling SetUserActive", "Handled SetUserActive"],
            host.AccessLogger.Collector.GetSnapshot().Select(record => record.Message));
    }

    [Fact]
    public async Task Activate_does_not_revoke_sessions_or_lock_links()
    {
        var host = new UserServiceTestHost();
        var user = host.Accounts.AddUser("user@example.com", isActive: false);

        var result = await host.Access.SetUserActiveAsync(user.Id, isActive: true, Ct);

        Assert.True(result.IsSuccess);
        Assert.True((await host.Accounts.FindByIdAsync(user.Id, Ct))!.IsActive);
        Assert.Empty(host.Links.LockedAccounts);
        Assert.Empty(host.SignIn.RevokedUsers);
        Assert.Equal(1, host.UnitOfWork.Commits);
    }

    [Fact]
    public async Task Deactivate_rejects_the_last_active_admin_without_mutation()
    {
        var host = new UserServiceTestHost();
        var admin = host.Accounts.AddUser("admin@example.com");
        host.Accounts.SetRoles(admin.Id, SystemRoles.Admin);

        var result = await host.Access.SetUserActiveAsync(admin.Id, isActive: false, Ct);

        Assert.Equal(UserErrors.LastAdmin, result.Error);
        Assert.True((await host.Accounts.FindByIdAsync(admin.Id, Ct))!.IsActive);
        Assert.Empty(host.SignIn.RevokedUsers);
        Assert.Equal(0, host.UnitOfWork.Commits);
        Assert.Equal(1, host.UnitOfWork.Rollbacks);
    }

    [Fact]
    public async Task Deactivate_rejects_the_current_user_without_mutation()
    {
        var host = new UserServiceTestHost();
        var me = host.Accounts.AddUser("me@example.com");
        host.CurrentUser.UserId = me.Id;

        var result = await host.Access.SetUserActiveAsync(me.Id, isActive: false, Ct);

        Assert.Equal(UserErrors.CannotModifySelf, result.Error);
        Assert.True((await host.Accounts.FindByIdAsync(me.Id, Ct))!.IsActive);
        Assert.Empty(host.SignIn.RevokedUsers);
        Assert.Equal(0, host.UnitOfWork.Commits);
        Assert.Equal(1, host.UnitOfWork.Rollbacks);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Missing_user_is_not_found_and_does_not_commit(bool isActive)
    {
        var host = new UserServiceTestHost();

        var result = await host.Access.SetUserActiveAsync(Guid.CreateVersion7(), isActive, Ct);

        Assert.Equal(UserErrors.NotFound, result.Error);
        Assert.Empty(host.SignIn.RevokedUsers);
        Assert.Equal(0, host.UnitOfWork.Commits);
        Assert.Equal(1, host.UnitOfWork.Rollbacks);
    }

    [Fact]
    public async Task Delete_locks_revokes_then_soft_deletes_and_commits_once()
    {
        var host = new UserServiceTestHost();
        var user = host.Accounts.AddUser("delete@example.com");

        var result = await host.Access.DeleteUserAsync(user.Id, Ct);

        Assert.True(result.IsSuccess);
        Assert.Equal([user.Id], host.Links.LockedAccounts);
        Assert.Equal([user.Id], host.SignIn.RevokedUsers);
        Assert.Null(await host.Accounts.FindByIdAsync(user.Id, Ct));
        Assert.Contains(host.Accounts.DeletedUsers, deleted => deleted.Id == user.Id);
        Assert.Equal(1, host.UnitOfWork.Commits);
    }

    [Fact]
    public async Task Delete_rejects_last_admin_without_revoking_or_deleting()
    {
        var host = new UserServiceTestHost();
        var admin = host.Accounts.AddUser("last-admin@example.com");
        host.Accounts.SetRoles(admin.Id, SystemRoles.Admin);

        var result = await host.Access.DeleteUserAsync(admin.Id, Ct);

        Assert.Equal(UserErrors.LastAdmin, result.Error);
        Assert.NotNull(await host.Accounts.FindByIdAsync(admin.Id, Ct));
        Assert.Empty(host.SignIn.RevokedUsers);
        Assert.Equal(0, host.UnitOfWork.Commits);
        Assert.Equal(1, host.UnitOfWork.Rollbacks);
    }

    [Fact]
    public async Task Deactivate_invalidates_the_pending_links_even_expired_ones()
    {
        var host = new UserServiceTestHost();
        var user = host.Accounts.AddUser("links@example.com");
        var now = host.Clock.GetUtcNow().UtcDateTime;
        var expired = LoginLink.Issue(user.Id, "hash-expired", now.AddHours(-1));
        host.Links.Links.Add(expired);

        var result = await host.Access.SetUserActiveAsync(user.Id, isActive: false, Ct);

        Assert.True(result.IsSuccess);
        Assert.Equal(now, expired.InvalidatedAtUtc);
        Assert.Equal(1, host.UnitOfWork.Commits);
    }

    [Fact]
    public async Task Delete_invalidates_the_pending_links()
    {
        var host = new UserServiceTestHost();
        var user = host.Accounts.AddUser("links-delete@example.com");
        var now = host.Clock.GetUtcNow().UtcDateTime;
        var active = LoginLink.Issue(user.Id, "hash-active", now.AddMinutes(-1));
        host.Links.Links.Add(active);

        var result = await host.Access.DeleteUserAsync(user.Id, Ct);

        Assert.True(result.IsSuccess);
        Assert.Equal(now, active.InvalidatedAtUtc);
    }
}
