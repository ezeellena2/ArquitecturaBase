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
    public void The_identifier_is_the_email_or_the_number_the_person_came_in_with()
    {
        var byEmail = LoginAudit.Success("ana@example.com", Guid.CreateVersion7(), LoginMethod.Code, null, null, Now);
        var byWhatsApp = LoginAudit.Failure("+5491123456789", null, LoginMethod.WhatsAppCode, "Auth.LoginCode.Invalid", null, null, Now);

        Assert.Equal("ana@example.com", byEmail.Identifier);
        Assert.Equal("+5491123456789", byWhatsApp.Identifier);
        Assert.Equal(LoginMethod.WhatsAppCode, byWhatsApp.Method);
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

    [Fact]
    public void Forgetting_the_request_info_clears_the_ip_and_the_browser_and_keeps_the_sign_in()
    {
        var userId = Guid.CreateVersion7();
        var audit = LoginAudit.Success("ana@example.com", userId, LoginMethod.Code, "10.0.0.1", "Firefox", Now);

        audit.ForgetRequestInfo();

        Assert.Null(audit.IpAddress);
        Assert.Null(audit.UserAgent);
        Assert.True(audit.Succeeded);
        Assert.Equal(userId, audit.UserId);
        Assert.Equal("ana@example.com", audit.Identifier);
        Assert.Equal(LoginMethod.Code, audit.Method);
        Assert.Equal(Now, audit.OccurredAtUtc);
    }

    [Fact]
    public void Forgetting_the_request_info_of_a_failure_also_clears_what_the_person_wrote()
    {
        // Lo escrito en un intento fallido puede ser el correo o el número de otra persona.
        var audit = LoginAudit.Failure("otra@example.com", null, LoginMethod.Code, "Auth.LoginCode.Invalid", "10.0.0.1", "Firefox", Now);

        audit.ForgetRequestInfo();

        Assert.Null(audit.Identifier);
        Assert.Null(audit.IpAddress);
        Assert.Null(audit.UserAgent);
        Assert.False(audit.Succeeded);
        Assert.Equal("Auth.LoginCode.Invalid", audit.FailureReason);
        Assert.Equal(Now, audit.OccurredAtUtc);
    }
}
