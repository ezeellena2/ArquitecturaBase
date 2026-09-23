using ArquitecturaBase.Application.Features.Auth;
using ArquitecturaBase.Application.Features.Auth.RequestLoginCode;
using ArquitecturaBase.Application.UnitTests.TestDoubles.Auth;
using ArquitecturaBase.Domain.Authentication;
using ArquitecturaBase.Domain.Settings;
using ArquitecturaBase.Domain.Users;
using ArquitecturaBase.Domain.ValueObjects;
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
    private readonly FakeSystemSettingsReader _settings = new();
    private readonly RequestLoginCodeCommandHandler _handler;

    public RequestLoginCodeCommandHandlerTests()
    {
        var options = Options.Create(new LoginCodeOptions());

        _handler = new RequestLoginCodeCommandHandler(
            new LoginCodeIssuer(_loginCodes, new FakeLoginCodeGenerator(), new FakeLoginCodeHasher(), options, _clock),
            _identity,
            _renderer,
            _emailQueue,
            _settings,
            options);
    }

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Issues_a_hashed_code_and_queues_the_email()
    {
        var result = await _handler.Handle(new RequestLoginCodeCommand(" Ana@Example.com "), Ct);

        Assert.True(result.IsSuccess);
        Assert.Equal(60, result.Value.ResendAfterSeconds);
        Assert.Equal([UserEmail], _loginCodes.LockedDestinations);

        var code = Assert.Single(_loginCodes.Codes);
        Assert.Equal(UserEmail, code.Destination);
        Assert.Equal(FakeLoginCodeHasher.HashOf(UserEmail, LoginCodePurpose.SignIn, FakeLoginCodeGenerator.Code), code.CodeHash);
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
    [Fact]
    public async Task Invite_only_issues_the_code_but_sends_no_email_for_an_unknown_email()
    {
        _settings.Mode = RegistrationMode.InviteOnly;

        var result = await _handler.Handle(new RequestLoginCodeCommand(UserEmail), Ct);

        // La misma respuesta de siempre: responder distinto diría qué direcciones están registradas.
        Assert.True(result.IsSuccess);
        Assert.Equal(60, result.Value.ResendAfterSeconds);

        // El código se emite igual: es lo que hace que los límites por dirección sigan valiendo.
        Assert.Single(_loginCodes.Codes);
        Assert.Empty(_emailQueue.Messages);
    }

    [Fact]
    public async Task Invite_only_still_emails_an_account_that_exists()
    {
        _settings.Mode = RegistrationMode.InviteOnly;
        _identity.AddUser(UserEmail);

        var result = await _handler.Handle(new RequestLoginCodeCommand(UserEmail), Ct);

        Assert.True(result.IsSuccess);
        Assert.Single(_loginCodes.Codes);
        Assert.Single(_emailQueue.Messages);
    }

    [Fact]
    public async Task Invite_only_applies_the_resend_limit_to_an_unknown_email_too()
    {
        _settings.Mode = RegistrationMode.InviteOnly;
        await _handler.Handle(new RequestLoginCodeCommand(UserEmail), Ct);
        _clock.Advance(TimeSpan.FromSeconds(20));

        var result = await _handler.Handle(new RequestLoginCodeCommand(UserEmail), Ct);

        // Exactamente el mismo error, y los mismos segundos, que recibe una dirección registrada al insistir.
        Assert.Equal(LoginCodeErrors.ResendTooSoonCode, result.Error.Code);
        Assert.Equal(40, result.Error.Metadata![LoginCodeErrors.RetryAfterKey]);
        Assert.Single(_loginCodes.Codes);
        Assert.Empty(_emailQueue.Messages);
    }

    [Fact]
    public async Task Issued_code_is_a_sign_in_code_by_email_marked_as_sent()
    {
        await _handler.Handle(new RequestLoginCodeCommand(UserEmail), Ct);

        var code = Assert.Single(_loginCodes.Codes);
        Assert.Equal(LoginCodeChannel.Email, code.Channel);
        Assert.Equal(LoginCodePurpose.SignIn, code.Purpose);
        Assert.Null(code.RequestedByUserId);
        Assert.Equal(_clock.GetUtcNow().UtcDateTime, code.SentAtUtc);
    }

    [Fact]
    public async Task Invite_only_leaves_the_code_of_an_unknown_email_unsent()
    {
        _settings.Mode = RegistrationMode.InviteOnly;

        await _handler.Handle(new RequestLoginCodeCommand(UserEmail), Ct);

        Assert.Null(Assert.Single(_loginCodes.Codes).SentAtUtc);
    }

    [Fact]
    public async Task A_recent_code_to_verify_the_same_email_holds_back_the_resend()
    {
        // Los límites son por destino, sea cual sea el propósito: protegen a quien recibe los mensajes.
        IssueCodeToVerifyTheEmail();
        _clock.Advance(TimeSpan.FromSeconds(20));

        var result = await _handler.Handle(new RequestLoginCodeCommand(UserEmail), Ct);

        Assert.Equal(LoginCodeErrors.ResendTooSoonCode, result.Error.Code);
        Assert.Equal(40, result.Error.Metadata![LoginCodeErrors.RetryAfterKey]);
        Assert.Single(_loginCodes.Codes);
        Assert.Empty(_emailQueue.Messages);
    }

    [Fact]
    public async Task Codes_to_verify_the_same_email_count_toward_the_request_limit()
    {
        for (var i = 0; i < 5; i++)
        {
            IssueCodeToVerifyTheEmail();
            _clock.Advance(TimeSpan.FromMinutes(1));
        }

        var result = await _handler.Handle(new RequestLoginCodeCommand(UserEmail), Ct);

        // El primero se pidió hace 5 minutos: sale de la ventana de 15 dentro de 10.
        Assert.Equal(LoginCodeErrors.TooManyRequestsCode, result.Error.Code);
        Assert.Equal(600, result.Error.Metadata![LoginCodeErrors.RetryAfterKey]);
        Assert.Equal(5, _loginCodes.Codes.Count);
        Assert.Empty(_emailQueue.Messages);
    }

    [Fact]
    public async Task A_new_sign_in_code_leaves_the_code_to_verify_the_email_active()
    {
        var verification = IssueCodeToVerifyTheEmail();
        _clock.Advance(TimeSpan.FromSeconds(60));

        var result = await _handler.Handle(new RequestLoginCodeCommand(UserEmail), Ct);

        Assert.True(result.IsSuccess);
        Assert.Null(verification.InvalidatedAtUtc);
        Assert.Null(_loginCodes.Codes[1].InvalidatedAtUtc);
    }

    // Un código que la cuenta pidió desde el perfil para vincular el mismo correo (lo emiten las tareas del perfil).
    private LoginCode IssueCodeToVerifyTheEmail()
    {
        var code = LoginCode.Issue(
            LoginCodeDestination.ForEmail(Email.Create(UserEmail).Value),
            LoginCodePurpose.VerifyDestination,
            requestedByUserId: Guid.CreateVersion7(),
            "hash-verify",
            _clock.GetUtcNow().UtcDateTime,
            TimeSpan.FromMinutes(10),
            maxAttempts: 5);

        _loginCodes.Add(code);

        return code;
    }
}
