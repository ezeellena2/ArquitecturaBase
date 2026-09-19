using ArquitecturaBase.Application.Features.Auth;
using ArquitecturaBase.Application.Features.Auth.RequestLoginCode;
using ArquitecturaBase.Application.UnitTests.TestDoubles.Auth;
using ArquitecturaBase.Domain.Authentication;
using ArquitecturaBase.Domain.Users;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;

namespace ArquitecturaBase.Application.UnitTests.Features.Auth;

public sealed class RequestLoginCodeCommandHandlerTests
{
    private const string UserEmail = "ana@example.com";

    private readonly FakeTimeProvider _clock = new(new DateTimeOffset(2026, 9, 19, 12, 0, 0, TimeSpan.Zero));
    private readonly InMemoryLoginCodeRepository _loginCodes = new();
    private readonly FakeIdentityService _identity = new();
    private readonly FakeEmailTemplateRenderer _renderer = new();
    private readonly FakeEmailQueue _emailQueue = new();
    private readonly RequestLoginCodeCommandHandler _handler;

    public RequestLoginCodeCommandHandlerTests()
    {
        _handler = new RequestLoginCodeCommandHandler(
            _loginCodes,
            _identity,
            new FakeLoginCodeGenerator(),
            new FakeLoginCodeHasher(),
            _renderer,
            _emailQueue,
            Options.Create(new LoginCodeOptions()),
            _clock);
    }

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Issues_a_hashed_code_and_queues_the_email()
    {
        var result = await _handler.Handle(new RequestLoginCodeCommand(" Ana@Example.com "), Ct);

        Assert.True(result.IsSuccess);
        Assert.Equal(60, result.Value.ResendAfterSeconds);
        Assert.Equal([UserEmail], _loginCodes.LockedEmails);

        var code = Assert.Single(_loginCodes.Codes);
        Assert.Equal(UserEmail, code.Email);
        Assert.Equal(FakeLoginCodeHasher.HashOf(UserEmail, FakeLoginCodeGenerator.Code), code.CodeHash);
        Assert.Equal(_clock.GetUtcNow().UtcDateTime.AddMinutes(10), code.ExpiresAtUtc);

        var message = Assert.Single(_emailQueue.Messages);
        Assert.Equal(UserEmail, message.To);
        Assert.Equal(FakeLoginCodeGenerator.Code, _renderer.LastCode);
    }

    [Fact]
    public async Task Email_uses_the_culture_of_the_existing_user()
    {
        _identity.AddUser(UserEmail, culture: "en");
        using var culture = new CultureScope("es");

        await _handler.Handle(new RequestLoginCodeCommand(UserEmail), Ct);

        Assert.Equal("en", _renderer.LastCulture!.Name);
    }

    [Fact]
    public async Task Email_for_a_new_account_uses_the_culture_of_the_request()
    {
        using var culture = new CultureScope("en");

        await _handler.Handle(new RequestLoginCodeCommand(UserEmail), Ct);

        Assert.Equal("en", _renderer.LastCulture!.Name);
    }

    [Fact]
    public async Task Asking_again_before_the_cooldown_returns_the_seconds_left()
    {
        await _handler.Handle(new RequestLoginCodeCommand(UserEmail), Ct);
        _clock.Advance(TimeSpan.FromSeconds(20));

        var result = await _handler.Handle(new RequestLoginCodeCommand(UserEmail), Ct);

        Assert.Equal(LoginCodeErrors.ResendTooSoonCode, result.Error.Code);
        Assert.Equal(40, result.Error.Metadata![LoginCodeErrors.RetryAfterKey]);
        Assert.Single(_loginCodes.Codes);
        Assert.Single(_emailQueue.Messages);
    }

    [Fact]
    public async Task New_code_invalidates_the_previous_ones()
    {
        await _handler.Handle(new RequestLoginCodeCommand(UserEmail), Ct);
        _clock.Advance(TimeSpan.FromSeconds(60));

        var result = await _handler.Handle(new RequestLoginCodeCommand(UserEmail), Ct);

        Assert.True(result.IsSuccess);
        Assert.Equal(2, _loginCodes.Codes.Count);
        Assert.NotNull(_loginCodes.Codes[0].InvalidatedAtUtc);
        Assert.Null(_loginCodes.Codes[1].InvalidatedAtUtc);
    }

    [Fact]
    public async Task Sixth_request_in_the_window_waits_until_the_oldest_one_leaves_it()
    {
        for (var i = 0; i < 5; i++)
        {
            Assert.True((await _handler.Handle(new RequestLoginCodeCommand(UserEmail), Ct)).IsSuccess);
            _clock.Advance(TimeSpan.FromMinutes(1));
        }

        var result = await _handler.Handle(new RequestLoginCodeCommand(UserEmail), Ct);

        // El primero se pidió hace 5 minutos: sale de la ventana de 15 dentro de 10.
        Assert.Equal(LoginCodeErrors.TooManyRequestsCode, result.Error.Code);
        Assert.Equal(600, result.Error.Metadata![LoginCodeErrors.RetryAfterKey]);
        Assert.Equal(5, _loginCodes.Codes.Count);
    }

    [Fact]
    public async Task Invalid_email_issues_nothing()
    {
        var result = await _handler.Handle(new RequestLoginCodeCommand("not-an-email"), Ct);

        Assert.Equal(UserErrors.EmailInvalidCode, result.Error.Code);
        Assert.Empty(_loginCodes.Codes);
        Assert.Empty(_emailQueue.Messages);
    }

    [Fact]
    public void Validator_requires_a_valid_email()
    {
        using var culture = new CultureScope("es");
        var validator = new RequestLoginCodeCommandValidator();

        var failure = Assert.Single(validator.Validate(new RequestLoginCodeCommand("ana@")).Errors);

        Assert.Equal("Ingresá un correo válido.", failure.ErrorMessage);
    }
}
