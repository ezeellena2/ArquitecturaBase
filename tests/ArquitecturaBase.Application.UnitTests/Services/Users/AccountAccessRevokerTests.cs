using ArquitecturaBase.Application.Services.Users;
using ArquitecturaBase.Application.UnitTests.TestDoubles.Auth;
using ArquitecturaBase.Domain.Authentication;
using Microsoft.Extensions.Time.Testing;

namespace ArquitecturaBase.Application.UnitTests.Services.Users;

public sealed class AccountAccessRevokerTests
{
    private readonly FakeTimeProvider _clock = new(new DateTimeOffset(2026, 9, 26, 12, 0, 0, TimeSpan.Zero));
    private readonly InMemoryLoginLinkRepository _links = new();
    private readonly FakeIdentityService _identity = new();

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private DateTime Now => _clock.GetUtcNow().UtcDateTime;

    [Fact]
    public async Task Invalidates_every_pending_link_of_the_account_including_expired_ones_and_revokes_the_sessions()
    {
        var userId = Guid.CreateVersion7();
        var expired = LoginLink.Issue(userId, "hash-expired", Now.AddHours(-1));
        var active = LoginLink.Issue(userId, "hash-active", Now.AddMinutes(-1));
        var consumed = LoginLink.Issue(userId, "hash-consumed", Now.AddMinutes(-2));
        Assert.True(consumed.Redeem(Now.AddMinutes(-1)).IsSuccess);
        var ofAnotherAccount = LoginLink.Issue(Guid.CreateVersion7(), "hash-other", Now.AddMinutes(-1));
        _links.Links.AddRange([expired, active, consumed, ofAnotherAccount]);

        await new AccountAccessRevoker(_links, _identity, _clock).RevokeAsync(userId, Ct);

        Assert.Equal(Now, expired.InvalidatedAtUtc);
        Assert.Equal(Now, active.InvalidatedAtUtc);
        Assert.Null(consumed.InvalidatedAtUtc);
        Assert.Null(ofAnotherAccount.InvalidatedAtUtc);
        Assert.Equal([userId], _identity.RevokedUsers);
    }
}
