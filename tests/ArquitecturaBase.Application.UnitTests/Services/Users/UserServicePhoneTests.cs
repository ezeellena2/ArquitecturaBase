using ArquitecturaBase.Domain.Authentication;
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
        Assert.Equal([user.Id], host.SignIn.RevokedUsers);
        Assert.Equal(1, host.UnitOfWork.Commits);
    }

    [Fact]
    public async Task Already_unlinked_account_commits_without_revoking_sessions()
    {
        var host = new UserServiceTestHost();
        var user = host.Identity.AddUser("no-phone@example.com");

        var result = await host.Service.UnlinkUserPhoneAsync(user.Id, Ct);

        Assert.True(result.IsSuccess);
        Assert.Empty(host.SignIn.RevokedUsers);
        Assert.Equal(1, host.UnitOfWork.Commits);
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
        Assert.Empty(host.SignIn.RevokedUsers);
        Assert.Equal(0, host.UnitOfWork.Commits);
        Assert.Equal(1, host.UnitOfWork.Rollbacks);
    }

    [Fact]
    public async Task Unlinking_a_number_invalidates_the_pending_links_even_expired_ones()
    {
        var host = new UserServiceTestHost();
        var user = host.Identity.AddUser("links-phone@example.com", phoneNumber: "+5491112345678");
        var now = host.Clock.GetUtcNow().UtcDateTime;
        var expired = LoginLink.Issue(user.Id, "hash-expired", now.AddHours(-1));
        host.Links.Links.Add(expired);

        var result = await host.Service.UnlinkUserPhoneAsync(user.Id, Ct);

        Assert.True(result.IsSuccess);
        Assert.Equal(now, expired.InvalidatedAtUtc);
    }
}
