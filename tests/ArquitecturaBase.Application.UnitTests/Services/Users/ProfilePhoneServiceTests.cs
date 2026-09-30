using ArquitecturaBase.Application.Interfaces.Persistence;
using ArquitecturaBase.Application.Models.Identity;
using ArquitecturaBase.Domain.Authentication;
using ArquitecturaBase.Domain.Users;

namespace ArquitecturaBase.Application.UnitTests.Services.Users;

/// <summary>La desvinculación del perfil sin canales: guardas, locks, enlaces y el límite, sin revocar sesiones.</summary>
public sealed class ProfilePhoneServiceTests
{
    private const string Phone = "+5493511234567";
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task The_only_login_method_cannot_be_unlinked()
    {
        var host = new UserServiceTestHost();
        var user = host.Accounts.AddUser(email: null, phoneNumber: Phone);
        host.CurrentUser.UserId = user.Id;
        var link = AddLink(host, user.Id);

        var result = await host.ProfilePhone.UnlinkOwnPhoneAsync(Ct);

        Assert.Equal(UserErrors.LastLoginMethod, result.Error);
        Assert.Equal(Phone, (await host.Accounts.FindByIdAsync(user.Id, Ct))!.PhoneNumber);
        Assert.Null(link.InvalidatedAtUtc);
        Assert.Equal(0, host.UnitOfWork.Commits);
        Assert.Equal(1, host.UnitOfWork.Rollbacks);
        Assert.Equal(CommitPolicy.OnSuccess, host.UnitOfWork.LastPolicy);
        Assert.Equal("UnlinkOwnPhone failed with " + UserErrors.LastLoginMethodCode,
            host.ProfilePhoneLogger.Collector.GetSnapshot()[^1].Message);
    }

    [Fact]
    public async Task Google_counts_as_another_login_method()
    {
        var host = new UserServiceTestHost();
        var user = host.Accounts.AddUser(email: null, phoneNumber: Phone);
        host.CurrentUser.UserId = user.Id;
        host.Accounts.LinkExternalLogin(user.Id, ExternalLoginProviders.Google, "google-123");

        var result = await host.ProfilePhone.UnlinkOwnPhoneAsync(Ct);

        Assert.True(result.IsSuccess);
        Assert.Null((await host.Accounts.FindByIdAsync(user.Id, Ct))!.PhoneNumber);
    }

    [Fact]
    public async Task Unlink_locks_the_participants_then_the_account_links_and_voids_only_the_active_links()
    {
        var host = new UserServiceTestHost();
        var user = host.Accounts.AddUser("ana@example.com", phoneNumber: Phone);
        host.CurrentUser.UserId = user.Id;
        var active = AddLink(host, user.Id);
        var expired = LoginLink.Issue(user.Id, "hash-expired", host.Clock.GetUtcNow().UtcDateTime.AddHours(-1));
        host.Links.Links.Add(expired);

        var result = await host.ProfilePhone.UnlinkOwnPhoneAsync(Ct);

        Assert.True(result.IsSuccess);
        Assert.Equal(["participant-lock:" + user.Id, "login-link:" + user.Id, "participant-released:" + user.Id],
            host.PhoneEvents);
        var updated = await host.Accounts.FindByIdAsync(user.Id, Ct);
        Assert.Null(updated!.PhoneNumber);
        Assert.False(updated.PhoneNumberConfirmed);
        Assert.NotNull(active.InvalidatedAtUtc);
        Assert.Null(expired.InvalidatedAtUtc);
        Assert.Equal(1, host.UnitOfWork.Transactions);
        Assert.Equal(1, host.UnitOfWork.Commits);
        Assert.Equal(CommitPolicy.OnSuccess, host.UnitOfWork.LastPolicy);
        Assert.Equal(["Handling UnlinkOwnPhone", "Handled UnlinkOwnPhone"],
            host.ProfilePhoneLogger.Collector.GetSnapshot().Select(record => record.Message));
    }

    [Fact]
    public async Task Unlink_without_a_number_still_notifies_the_participants_and_voids_the_links()
    {
        var host = new UserServiceTestHost();
        var user = host.Accounts.AddUser(email: null);
        host.CurrentUser.UserId = user.Id;
        var link = AddLink(host, user.Id);

        var result = await host.ProfilePhone.UnlinkOwnPhoneAsync(Ct);

        Assert.True(result.IsSuccess);
        Assert.NotNull(link.InvalidatedAtUtc);
        Assert.Equal([user.Id], host.Links.LockedAccounts);
        Assert.Contains("participant-released:" + user.Id, host.PhoneEvents);
        Assert.Equal(1, host.UnitOfWork.Commits);
    }

    [Fact]
    public async Task Unlink_reads_the_account_after_the_locks()
    {
        var host = new UserServiceTestHost();
        var user = host.Accounts.AddUser("ana@example.com", phoneNumber: Phone);
        host.CurrentUser.UserId = user.Id;
        host.Links.WhileWaitingForTheLock = userId =>
            host.Accounts.ArrangeAsync(accounts => accounts.DeleteAsync(userId, Ct));

        var result = await host.ProfilePhone.UnlinkOwnPhoneAsync(Ct);

        Assert.Equal(UserErrors.NotFound, result.Error);
        Assert.Equal(0, host.UnitOfWork.Commits);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task An_absent_session_or_account_cannot_unlink_a_number(bool hasSession)
    {
        var host = new UserServiceTestHost();
        host.CurrentUser.UserId = hasSession ? Guid.CreateVersion7() : null;

        var result = await host.ProfilePhone.UnlinkOwnPhoneAsync(Ct);

        Assert.Equal(UserErrors.NotFound, result.Error);
        Assert.Equal(1, host.UnitOfWork.Transactions);
        Assert.Equal(0, host.UnitOfWork.Commits);
        Assert.Equal(1, host.UnitOfWork.Rollbacks);
    }

    private static LoginLink AddLink(UserServiceTestHost host, Guid userId)
    {
        var link = LoginLink.Issue(userId, "hash", host.Clock.GetUtcNow().UtcDateTime);
        host.Links.Links.Add(link);
        return link;
    }
}
