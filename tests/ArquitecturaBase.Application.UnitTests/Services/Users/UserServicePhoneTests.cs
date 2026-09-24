using ArquitecturaBase.Domain.Users;

namespace ArquitecturaBase.Application.UnitTests.Services.Users;

public sealed class UserServicePhoneTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Unlink_locks_contact_then_account_revokes_sessions_and_commits()
    {
        var host = new UserServiceTestHost();
        var user = host.Identity.AddUser("phone@example.com", phoneNumber: "+5491112345678");

        var result = await host.Service.UnlinkUserPhoneAsync(user.Id, Ct);

        Assert.True(result.IsSuccess);
        Assert.Null((await host.Identity.FindByIdAsync(user.Id, Ct))!.PhoneNumber);
        Assert.Equal(["number-change:" + user.Id], host.MessagesLog.Keys);
        Assert.Equal([user.Id], host.Links.LockedAccounts);
        Assert.Equal([user.Id], host.Identity.RevokedUsers);
        Assert.Equal(1, host.UnitOfWork.SaveChangesCalls);
    }

    [Fact]
    public async Task Already_unlinked_account_commits_without_revoking_sessions()
    {
        var host = new UserServiceTestHost();
        var user = host.Identity.AddUser("no-phone@example.com");

        var result = await host.Service.UnlinkUserPhoneAsync(user.Id, Ct);

        Assert.True(result.IsSuccess);
        Assert.Empty(host.Identity.RevokedUsers);
        Assert.Equal(1, host.UnitOfWork.SaveChangesCalls);
    }

    [Fact]
    public async Task Own_only_login_method_cannot_be_unlinked()
    {
        var host = new UserServiceTestHost();
        var user = host.Identity.AddUser(null, phoneNumber: "+5491112345678");
        host.CurrentUser.UserId = user.Id;

        var result = await host.Service.UnlinkUserPhoneAsync(user.Id, Ct);

        Assert.Equal(UserErrors.LastLoginMethod, result.Error);
        Assert.NotNull((await host.Identity.FindByIdAsync(user.Id, Ct))!.PhoneNumber);
        Assert.Empty(host.Identity.RevokedUsers);
        Assert.Equal(0, host.UnitOfWork.SaveChangesCalls);
    }
}
