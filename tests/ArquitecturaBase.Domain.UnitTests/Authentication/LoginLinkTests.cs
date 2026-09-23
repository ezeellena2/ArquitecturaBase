using ArquitecturaBase.Domain.Authentication;

namespace ArquitecturaBase.Domain.UnitTests.Authentication;

public sealed class LoginLinkTests
{
    private const string Hash = "HASH-OK";
    private static readonly DateTime Now = new(2026, 9, 23, 12, 0, 0, DateTimeKind.Utc);
    private static readonly Guid Owner = Guid.CreateVersion7();

    private static LoginLink Issue() => LoginLink.Issue(Owner, Hash, Now);

    [Fact]
    public void Issued_link_belongs_to_the_account_and_stays_active_for_ten_minutes()
    {
        var link = Issue();

        Assert.Equal(Owner, link.UserId);
        Assert.Equal(Hash, link.TokenHash);
        Assert.Equal(Now, link.CreatedAtUtc);
        Assert.Equal(Now.AddMinutes(10), link.ExpiresAtUtc);
        Assert.Null(link.ConsumedAtUtc);
        Assert.Null(link.InvalidatedAtUtc);
        Assert.True(link.IsActive(Now.AddMinutes(10).AddTicks(-1)));
        Assert.False(link.IsActive(Now.AddMinutes(10)));
    }

    [Fact]
    public void Issue_rejects_a_link_without_an_account_or_a_hash()
    {
        Assert.Throws<ArgumentException>(() => LoginLink.Issue(Guid.Empty, Hash, Now));
        Assert.Throws<ArgumentException>(() => LoginLink.Issue(Owner, " ", Now));
        Assert.Throws<ArgumentNullException>(() => LoginLink.Issue(Owner, null!, Now));
    }

    [Fact]
    public void Active_link_is_consumed_when_redeemed()
    {
        var link = Issue();
        var redeemedAt = Now.AddMinutes(3);

        var result = link.Redeem(redeemedAt);

        Assert.True(result.IsSuccess);
        Assert.Equal(redeemedAt, link.ConsumedAtUtc);
        Assert.False(link.IsActive(redeemedAt));
    }

    [Fact]
    public void Link_is_redeemed_only_once()
    {
        var link = Issue();
        link.Redeem(Now.AddMinutes(1));

        var second = link.Redeem(Now.AddMinutes(2));

        Assert.Equal(LoginLinkErrors.Invalid, second.Error);
        Assert.Equal(Now.AddMinutes(1), link.ConsumedAtUtc);
    }

    [Fact]
    public void Expired_link_is_rejected_and_not_consumed()
    {
        var link = Issue();

        var result = link.Redeem(Now.AddMinutes(10));

        Assert.Equal(LoginLinkErrors.Invalid, result.Error);
        Assert.Null(link.ConsumedAtUtc);
    }

    [Fact]
    public void Invalidated_link_is_rejected_and_not_consumed()
    {
        var link = Issue();
        link.Invalidate(Now.AddMinutes(1));

        var result = link.Redeem(Now.AddMinutes(2));

        Assert.Equal(LoginLinkErrors.Invalid, result.Error);
        Assert.Null(link.ConsumedAtUtc);
        Assert.False(link.IsActive(Now.AddMinutes(2)));
    }

    [Fact]
    public void Invalidate_keeps_the_first_moment()
    {
        var link = Issue();

        link.Invalidate(Now.AddMinutes(1));
        link.Invalidate(Now.AddMinutes(5));

        Assert.Equal(Now.AddMinutes(1), link.InvalidatedAtUtc);
    }

    [Fact]
    public void Expired_used_and_invalidated_links_fail_with_the_same_error()
    {
        var expired = Issue();
        var used = Issue();
        used.Redeem(Now);
        var invalidated = Issue();
        invalidated.Invalidate(Now);

        var errors = new[]
        {
            expired.Redeem(Now.AddMinutes(11)).Error,
            used.Redeem(Now.AddMinutes(1)).Error,
            invalidated.Redeem(Now.AddMinutes(1)).Error,
        };

        // Nada del error dice qué le pasó al enlace: ni el código, ni la descripción ni la metadata.
        Assert.All(errors, error => Assert.Equal(LoginLinkErrors.Invalid, error));
        Assert.Equal(LoginLinkErrors.InvalidCode, errors[0].Code);
        Assert.Null(errors[0].Metadata);
    }

    [Fact]
    public void Too_many_requests_error_carries_the_seconds_to_wait()
    {
        var error = LoginLinkErrors.TooManyRequests(42);

        Assert.Equal(LoginLinkErrors.TooManyRequestsCode, error.Code);
        Assert.Equal(42, error.Metadata![LoginLinkErrors.RetryAfterKey]);
    }
}
