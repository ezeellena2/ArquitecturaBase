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
    public void Invalidated_link_is_no_longer_active()
    {
        var link = Issue();

        link.Invalidate(Now.AddMinutes(1));

        Assert.False(link.IsActive(Now.AddMinutes(2)));
        Assert.Null(link.ConsumedAtUtc);
    }

    [Fact]
    public void Invalidate_keeps_the_first_moment()
    {
        var link = Issue();

        link.Invalidate(Now.AddMinutes(1));
        link.Invalidate(Now.AddMinutes(5));

        Assert.Equal(Now.AddMinutes(1), link.InvalidatedAtUtc);
    }
}
