using ArquitecturaBase.Application.Abstractions.Identity;
using ArquitecturaBase.Application.Features.Auth;
using ArquitecturaBase.Application.Features.Auth.SignInWithExternalProvider;
using ArquitecturaBase.Application.UnitTests.TestDoubles.Auth;
using ArquitecturaBase.Domain.Authentication;
using ArquitecturaBase.Domain.Settings;
using Microsoft.Extensions.Time.Testing;

namespace ArquitecturaBase.Application.UnitTests.Features.Auth;

public sealed class SignInWithExternalProviderCommandHandlerTests
{
    private const string UserEmail = "ana@example.com";
    private const string ReturnUrl = "/connect/authorize?client_id=web";

    private readonly FakeTimeProvider _clock = new(new DateTimeOffset(2026, 9, 19, 12, 0, 0, TimeSpan.Zero));
    private readonly InMemoryLoginAuditRepository _audits = new();
    private readonly FakeIdentityService _identity = new();
    private readonly FakeSystemSettingsReader _settings = new();
    private readonly FakeInitialAdmin _initialAdmin = new();
    private readonly SignInWithExternalProviderCommandHandler _handler;

    public SignInWithExternalProviderCommandHandlerTests()
    {
        _handler = new SignInWithExternalProviderCommandHandler(
            _identity, _audits, new AccountCreationPolicy(_settings, _initialAdmin), new FakeRequestInfo(), _clock);
    }

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Linked_account_signs_in()
    {
        var user = _identity.AddUser(UserEmail);
        _identity.LinkExternalLogin(user.Id, "Google", "google-123");
        _identity.PendingExternalLogin = GoogleLogin(emailVerified: true);

        var result = await _handler.Handle(new SignInWithExternalProviderCommand(ReturnUrl), Ct);

        Assert.Equal(ReturnUrl, result.Value.ReturnUrl);
        Assert.Equal([user.Id], _identity.SignedInUsers);
        Assert.True(_identity.ExternalSignedOut);

        var audit = Assert.Single(_audits.Audits);
        Assert.True(audit.Succeeded);
        Assert.Equal(LoginMethod.Google, audit.Method);
    }

    [Fact]
    public async Task Verified_email_without_an_account_creates_it_and_links_the_login()
    {
        _identity.PendingExternalLogin = GoogleLogin(emailVerified: true);

        var result = await _handler.Handle(new SignInWithExternalProviderCommand(ReturnUrl), Ct);

        Assert.True(result.IsSuccess);
        var user = Assert.Single(_identity.Users);
        Assert.Equal("Ana Pérez", user.DisplayName);
        Assert.Same(user, await _identity.FindByExternalLoginAsync("Google", "google-123", Ct));
    }

    [Fact]
    public async Task Verified_email_of_an_existing_account_links_the_login_without_duplicating_it()
    {
        var user = _identity.AddUser(UserEmail);
        _identity.PendingExternalLogin = GoogleLogin(emailVerified: true);

        await _handler.Handle(new SignInWithExternalProviderCommand(ReturnUrl), Ct);

        Assert.Single(_identity.Users);
        Assert.Same(user, await _identity.FindByExternalLoginAsync("Google", "google-123", Ct));
        Assert.Equal([user.Id], _identity.SignedInUsers);
    }

    [Fact]
    public async Task Linking_the_account_verifies_the_email_that_an_administrator_loaded()
    {
        // Google ya verificó la dirección: es lo mismo que entrar con el código que llegó ahí.
        var user = await _identity.CreateUnverifiedAsync(
            Domain.ValueObjects.Email.Create(UserEmail).Value, phone: null, "Laura", "es", Ct);
        _identity.PendingExternalLogin = GoogleLogin(emailVerified: true);

        var result = await _handler.Handle(new SignInWithExternalProviderCommand(ReturnUrl), Ct);

        Assert.True(result.IsSuccess);
        Assert.True(Assert.Single(_identity.Users).EmailConfirmed);
        Assert.Equal([user.Id], _identity.SignedInUsers);
    }

    [Fact]
    public async Task Unverified_email_is_rejected_without_creating_or_linking()
    {
        _identity.AddUser(UserEmail);
        _identity.PendingExternalLogin = GoogleLogin(emailVerified: false);

        var result = await _handler.Handle(new SignInWithExternalProviderCommand(ReturnUrl), Ct);

        Assert.Equal(ExternalLoginErrors.EmailNotVerifiedCode, result.Error.Code);
        Assert.Single(_identity.Users);
        Assert.Null(await _identity.FindByExternalLoginAsync("Google", "google-123", Ct));
        Assert.Empty(_identity.SignedInUsers);
        Assert.True(_identity.ExternalSignedOut);
        Assert.False(Assert.Single(_audits.Audits).Succeeded);
    }

    [Fact]
    public async Task Missing_external_login_fails()
    {
        var result = await _handler.Handle(new SignInWithExternalProviderCommand(ReturnUrl), Ct);

        Assert.Equal(ExternalLoginErrors.FailedCode, result.Error.Code);
        Assert.Single(_audits.Audits);
    }

    [Fact]
    public async Task Disabled_account_cannot_sign_in()
    {
        var user = _identity.AddUser(UserEmail, isActive: false);
        _identity.LinkExternalLogin(user.Id, "Google", "google-123");
        _identity.PendingExternalLogin = GoogleLogin(emailVerified: true);

        var result = await _handler.Handle(new SignInWithExternalProviderCommand(ReturnUrl), Ct);

        Assert.Equal(AccountErrors.DisabledCode, result.Error.Code);
        Assert.Empty(_identity.SignedInUsers);
    }

    [Fact]
    public async Task Invite_only_rejects_an_email_without_an_account_and_creates_nothing()
    {
        _settings.Mode = RegistrationMode.InviteOnly;
        _identity.PendingExternalLogin = GoogleLogin(emailVerified: true);

        var result = await _handler.Handle(new SignInWithExternalProviderCommand(ReturnUrl), Ct);

        Assert.Equal(AccountErrors.NotInvitedCode, result.Error.Code);
        Assert.Empty(_identity.Users);
        Assert.Empty(_identity.SignedInUsers);
        Assert.True(_identity.ExternalSignedOut);

        var audit = Assert.Single(_audits.Audits);
        Assert.False(audit.Succeeded);
        Assert.Equal(AccountErrors.NotInvitedCode, audit.FailureReason);
        Assert.Equal(UserEmail, audit.Identifier);
    }

    [Fact]
    public async Task Invite_only_lets_in_an_account_that_an_administrator_already_created()
    {
        _settings.Mode = RegistrationMode.InviteOnly;
        var user = _identity.AddUser(UserEmail);
        _identity.PendingExternalLogin = GoogleLogin(emailVerified: true);

        var result = await _handler.Handle(new SignInWithExternalProviderCommand(ReturnUrl), Ct);

        Assert.True(result.IsSuccess);
        Assert.Equal([user.Id], _identity.SignedInUsers);
        Assert.Same(user, await _identity.FindByExternalLoginAsync("Google", "google-123", Ct));
    }

    [Fact]
    public async Task Invite_only_creates_the_account_of_the_initial_admin_and_links_the_login()
    {
        // Sin esto, una base nueva en InviteOnly no deja entrar a nadie: no hay otro administrador que lo dé de alta.
        _settings.Mode = RegistrationMode.InviteOnly;
        _identity.PendingExternalLogin = GoogleLogin(emailVerified: true, FakeInitialAdmin.DefaultEmail);

        var result = await _handler.Handle(new SignInWithExternalProviderCommand(ReturnUrl), Ct);

        Assert.True(result.IsSuccess);
        var admin = Assert.Single(_identity.Users);
        Assert.Equal(FakeInitialAdmin.DefaultEmail, admin.Email);
        Assert.Same(admin, await _identity.FindByExternalLoginAsync("Google", "google-123", Ct));
        Assert.Equal([admin.Id], _identity.SignedInUsers);
        Assert.True(Assert.Single(_audits.Audits).Succeeded);
    }

    [Fact]
    public async Task Invite_only_reports_the_deleted_account_of_the_initial_admin_as_disabled()
    {
        _settings.Mode = RegistrationMode.InviteOnly;
        _identity.DeletedEmails.Add(FakeInitialAdmin.DefaultEmail);
        _identity.PendingExternalLogin = GoogleLogin(emailVerified: true, FakeInitialAdmin.DefaultEmail);

        var result = await _handler.Handle(new SignInWithExternalProviderCommand(ReturnUrl), Ct);

        Assert.Equal(AccountErrors.DisabledCode, result.Error.Code);
        Assert.Empty(_identity.Users);
        Assert.Empty(_identity.SignedInUsers);
        Assert.Equal(AccountErrors.DisabledCode, Assert.Single(_audits.Audits).FailureReason);
    }

    [Fact]
    public async Task A_linked_account_without_email_is_audited_with_the_email_that_google_sent()
    {
        // Con Google, la auditoría identifica el ingreso con un correo: una cuenta de solo número usa el de Google.
        var user = _identity.AddUser(email: null, phoneNumber: "+5493511234567");
        _identity.LinkExternalLogin(user.Id, "Google", "google-123");
        _identity.PendingExternalLogin = GoogleLogin(emailVerified: true);

        var result = await _handler.Handle(new SignInWithExternalProviderCommand(ReturnUrl), Ct);

        Assert.True(result.IsSuccess);
        Assert.Equal(UserEmail, Assert.Single(_audits.Audits).Identifier);
    }

    private static ExternalLogin GoogleLogin(bool emailVerified, string email = "Ana@Example.com") =>
        new("Google", "google-123", email, emailVerified, "Ana Pérez");
}
