using ArquitecturaBase.Domain.Authentication;

namespace ArquitecturaBase.Domain.UnitTests.Authentication;

public sealed class LoginAuditTests
{
    private static readonly DateTime Now = new(2026, 9, 19, 12, 0, 0, DateTimeKind.Utc);

    [Fact]
    public void Success_records_the_user_and_no_reason()
    {
        var userId = Guid.CreateVersion7();

        var audit = LoginAudit.Success("ana@example.com", userId, LoginMethod.Code, "10.0.0.1", "Firefox", Now);

        Assert.True(audit.Succeeded);
        Assert.Equal(userId, audit.UserId);
        Assert.Equal(LoginMethod.Code, audit.Method);
        Assert.Null(audit.FailureReason);
        Assert.Equal("10.0.0.1", audit.IpAddress);
        Assert.Equal(Now, audit.OccurredAtUtc);
    }

    [Fact]
    public void Failure_records_the_reason()
    {
        var audit = LoginAudit.Failure("ana@example.com", null, LoginMethod.Google, "Auth.LoginCode.Invalid", null, null, Now);

        Assert.False(audit.Succeeded);
        Assert.Null(audit.UserId);
        Assert.Equal("Auth.LoginCode.Invalid", audit.FailureReason);
    }

    [Fact]
    public void Failure_requires_a_reason()
    {
        Assert.Throws<ArgumentException>(() =>
            LoginAudit.Failure("ana@example.com", null, LoginMethod.Code, " ", null, null, Now));
    }

    [Fact]
    public void Long_user_agents_are_truncated()
    {
        var audit = LoginAudit.Success(
            "ana@example.com", Guid.CreateVersion7(), LoginMethod.Code, null, new string('x', 2000), Now);

        Assert.Equal(LoginAudit.MaxUserAgentLength, audit.UserAgent!.Length);
    }
}
