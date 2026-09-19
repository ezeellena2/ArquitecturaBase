using ArquitecturaBase.Application.Features.Auth.VerifyLoginCode;
using ArquitecturaBase.Application.UnitTests.TestDoubles.Auth;
using ArquitecturaBase.Domain.Authentication;
using ArquitecturaBase.Domain.ValueObjects;
using Microsoft.Extensions.Time.Testing;

namespace ArquitecturaBase.Application.UnitTests.Features.Auth;

public sealed class VerifyLoginCodeCommandHandlerTests
{
    private const string UserEmail = "ana@example.com";
    private const string RightCode = "123456";
    private const string ReturnUrl = "/connect/authorize?client_id=web";

    private readonly FakeTimeProvider _clock = new(new DateTimeOffset(2026, 9, 19, 12, 0, 0, TimeSpan.Zero));
    private readonly InMemoryLoginCodeRepository _loginCodes = new();
    private readonly InMemoryLoginAuditRepository _audits = new();
    private readonly FakeIdentityService _identity = new();
    private readonly VerifyLoginCodeCommandHandler _handler;

    public VerifyLoginCodeCommandHandlerTests()
    {
        _handler = new VerifyLoginCodeCommandHandler(
            _loginCodes, _audits, _identity, new FakeLoginCodeHasher(), new FakeRequestInfo(), _clock);
    }

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Right_code_for_a_new_email_creates_the_account_and_signs_in()
    {
        IssueCode();
        using var culture = new CultureScope("en");

        var result = await _handler.Handle(Command(RightCode), Ct);

        Assert.True(result.IsSuccess);
        Assert.Equal(ReturnUrl, result.Value.ReturnUrl);
        Assert.Equal([UserEmail], _loginCodes.LockedEmails);
        var user = Assert.Single(_identity.Users);
        Assert.Equal("en", user.Culture);
        Assert.Equal([user.Id], _identity.SignedInUsers);
        Assert.NotNull(_loginCodes.Codes[0].ConsumedAtUtc);

        var audit = Assert.Single(_audits.Audits);
        Assert.True(audit.Succeeded);
        Assert.Equal(user.Id, audit.UserId);
        Assert.Equal(LoginMethod.Code, audit.Method);
        Assert.Equal("203.0.113.10", audit.IpAddress);
    }

    [Fact]
    public async Task Right_code_for_an_existing_user_resets_the_failed_attempts()
    {
        var user = _identity.AddUser(UserEmail);
        _identity.FailedAttempts[user.Id] = 3;
        IssueCode();

        var result = await _handler.Handle(Command(RightCode), Ct);

        Assert.True(result.IsSuccess);
        Assert.Single(_identity.Users);
        Assert.Equal(0, _identity.FailedAttempts[user.Id]);
        Assert.Equal([user.Id], _identity.SignedInUsers);
    }

    [Fact]
    public async Task Wrong_code_counts_a_failed_attempt_and_is_audited()
    {
        var user = _identity.AddUser(UserEmail);
        IssueCode();

        var result = await _handler.Handle(Command("000000"), Ct);

        Assert.Equal(LoginCodeErrors.InvalidCode, result.Error.Code);
        Assert.Equal(4, result.Error.Metadata![LoginCodeErrors.AttemptsLeftKey]);
        Assert.Equal(1, _identity.FailedAttempts[user.Id]);
        Assert.Empty(_identity.SignedInUsers);

        var audit = Assert.Single(_audits.Audits);
        Assert.False(audit.Succeeded);
        Assert.Equal(LoginCodeErrors.InvalidCode, audit.FailureReason);
        Assert.Equal(user.Id, audit.UserId);
    }

    [Fact]
    public async Task Without_a_code_the_error_is_the_same_as_for_a_wrong_code()
    {
        var result = await _handler.Handle(Command(RightCode), Ct);

        Assert.Equal(LoginCodeErrors.InvalidCode, result.Error.Code);
        Assert.Null(result.Error.Metadata);
        Assert.Empty(_identity.Users);
        Assert.Single(_audits.Audits);
    }

    [Fact]
    public async Task Locked_out_user_is_rejected_before_checking_the_code()
    {
        var user = _identity.AddUser(UserEmail);
        _identity.LockedOutUsers.Add(user.Id);
        IssueCode();

        var result = await _handler.Handle(Command(RightCode), Ct);

        Assert.Equal(AccountErrors.LockedOutCode, result.Error.Code);
        Assert.Null(_loginCodes.Codes[0].ConsumedAtUtc);
        Assert.Equal(0, _loginCodes.Codes[0].FailedAttempts);
        Assert.Equal(AccountErrors.LockedOutCode, Assert.Single(_audits.Audits).FailureReason);
    }

    [Fact]
    public async Task Disabled_account_is_reported_after_the_code_is_verified()
    {
        _identity.AddUser(UserEmail, isActive: false);
        IssueCode();

        var result = await _handler.Handle(Command(RightCode), Ct);

        Assert.Equal(AccountErrors.DisabledCode, result.Error.Code);
        Assert.NotNull(_loginCodes.Codes[0].ConsumedAtUtc);
        Assert.Empty(_identity.SignedInUsers);
        Assert.Equal(AccountErrors.DisabledCode, Assert.Single(_audits.Audits).FailureReason);
    }

    [Fact]
    public async Task Expired_code_is_rejected()
    {
        IssueCode();
        _clock.Advance(TimeSpan.FromMinutes(10));

        var result = await _handler.Handle(Command(RightCode), Ct);

        Assert.Equal(LoginCodeErrors.ExpiredCode, result.Error.Code);
        Assert.Empty(_identity.Users);
    }

    private static VerifyLoginCodeCommand Command(string code) => new(UserEmail, code, ReturnUrl);

    private void IssueCode() =>
        _loginCodes.Add(LoginCode.Issue(
            Email.Create(UserEmail).Value,
            FakeLoginCodeHasher.HashOf(UserEmail, RightCode),
            _clock.GetUtcNow().UtcDateTime,
            TimeSpan.FromMinutes(10),
            maxAttempts: 5));
}
