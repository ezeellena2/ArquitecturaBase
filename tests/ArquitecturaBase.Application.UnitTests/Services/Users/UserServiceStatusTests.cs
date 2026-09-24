using ArquitecturaBase.Domain.Authorization;
using ArquitecturaBase.Domain.Users;

namespace ArquitecturaBase.Application.UnitTests.Services.Users;

public sealed class UserServiceStatusTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Deactivate_locks_account_revokes_sessions_and_commits_once()
    {
        var host = new UserServiceTestHost();
        var user = host.Identity.AddUser("user@example.com");

        var result = await host.Service.SetUserActiveAsync(user.Id, isActive: false, Ct);

        Assert.True(result.IsSuccess);
        Assert.False((await host.Identity.FindByIdAsync(user.Id, Ct))!.IsActive);
        Assert.Equal([user.Id], host.Links.LockedAccounts);
        Assert.Equal([user.Id], host.Identity.RevokedUsers);
        Assert.Equal(1, host.UnitOfWork.SaveChangesCalls);
        Assert.Equal(["Handling SetUserActiveCommand", "Handled SetUserActiveCommand"],
            host.Logger.Collector.GetSnapshot().Select(record => record.Message));
    }

    [Fact]
    public async Task Activate_does_not_revoke_sessions_or_lock_links()
    {
        var host = new UserServiceTestHost();
        var user = host.Identity.AddUser("user@example.com", isActive: false);

        var result = await host.Service.SetUserActiveAsync(user.Id, isActive: true, Ct);

        Assert.True(result.IsSuccess);
        Assert.True((await host.Identity.FindByIdAsync(user.Id, Ct))!.IsActive);
        Assert.Empty(host.Links.LockedAccounts);
        Assert.Empty(host.Identity.RevokedUsers);
        Assert.Equal(1, host.UnitOfWork.SaveChangesCalls);
    }

    [Fact]
    public async Task Deactivate_rejects_the_last_active_admin_without_mutation()
    {
        var host = new UserServiceTestHost();
        var admin = host.Identity.AddUser("admin@example.com");
        host.Identity.SetRoles(admin.Id, SystemRoles.Admin);

        var result = await host.Service.SetUserActiveAsync(admin.Id, isActive: false, Ct);

        Assert.Equal(UserErrors.LastAdmin, result.Error);
        Assert.True((await host.Identity.FindByIdAsync(admin.Id, Ct))!.IsActive);
        Assert.Empty(host.Identity.RevokedUsers);
        Assert.Equal(0, host.UnitOfWork.SaveChangesCalls);
    }

    [Fact]
    public async Task Deactivate_rejects_the_current_user_without_mutation()
    {
        var host = new UserServiceTestHost();
        var me = host.Identity.AddUser("me@example.com");
        host.CurrentUser.UserId = me.Id;

        var result = await host.Service.SetUserActiveAsync(me.Id, isActive: false, Ct);

        Assert.Equal(UserErrors.CannotModifySelf, result.Error);
        Assert.True((await host.Identity.FindByIdAsync(me.Id, Ct))!.IsActive);
        Assert.Empty(host.Identity.RevokedUsers);
        Assert.Equal(0, host.UnitOfWork.SaveChangesCalls);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Missing_user_is_not_found_and_does_not_commit(bool isActive)
    {
        var host = new UserServiceTestHost();

        var result = await host.Service.SetUserActiveAsync(Guid.CreateVersion7(), isActive, Ct);

        Assert.Equal(UserErrors.NotFound, result.Error);
        Assert.Empty(host.Identity.RevokedUsers);
        Assert.Equal(0, host.UnitOfWork.SaveChangesCalls);
    }

    [Fact]
    public async Task Delete_locks_revokes_then_soft_deletes_and_commits_once()
    {
        var host = new UserServiceTestHost();
        var user = host.Identity.AddUser("delete@example.com");

        var result = await host.Service.DeleteUserAsync(user.Id, Ct);

        Assert.True(result.IsSuccess);
        Assert.Equal([user.Id], host.Links.LockedAccounts);
        Assert.Equal([user.Id], host.Identity.RevokedUsers);
        Assert.Null(await host.Identity.FindByIdAsync(user.Id, Ct));
        Assert.Contains(host.Identity.DeletedUsers, deleted => deleted.Id == user.Id);
        Assert.Equal(1, host.UnitOfWork.SaveChangesCalls);
    }

    [Fact]
    public async Task Delete_rejects_last_admin_without_revoking_or_deleting()
    {
        var host = new UserServiceTestHost();
        var admin = host.Identity.AddUser("last-admin@example.com");
        host.Identity.SetRoles(admin.Id, SystemRoles.Admin);

        var result = await host.Service.DeleteUserAsync(admin.Id, Ct);

        Assert.Equal(UserErrors.LastAdmin, result.Error);
        Assert.NotNull(await host.Identity.FindByIdAsync(admin.Id, Ct));
        Assert.Empty(host.Identity.RevokedUsers);
        Assert.Equal(0, host.UnitOfWork.SaveChangesCalls);
    }
}
