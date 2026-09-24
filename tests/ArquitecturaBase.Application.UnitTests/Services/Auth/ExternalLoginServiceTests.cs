using ArquitecturaBase.Application.Common.Validation;
using ArquitecturaBase.Application.Services.Auth;
using ArquitecturaBase.Application.Models.Auth;
using ArquitecturaBase.Application.Models.Identity;
using ArquitecturaBase.Application.UnitTests.TestDoubles;
using ArquitecturaBase.Application.UnitTests.TestDoubles.Auth;
using ArquitecturaBase.Application.Validation.Auth;
using ArquitecturaBase.Domain.Authentication;
using ArquitecturaBase.Domain.Settings;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;

namespace ArquitecturaBase.Application.UnitTests.Services.Auth;

public sealed class ExternalLoginServiceTests
{
    private const string ReturnUrl = "/connect/authorize?client_id=web";
    private const string UserEmail = "ana@example.com";

    private readonly FakeIdentityService _identity = new();
    private readonly InMemoryLoginAuditRepository _audits = new();
    private readonly FakeSystemSettingsReader _settings = new();
    private readonly FakeUnitOfWork _unitOfWork = new();
    private readonly FakeTimeProvider _time = new(new DateTimeOffset(2026, 9, 19, 12, 0, 0, TimeSpan.Zero));

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Linked_account_signs_in_and_persists_a_successful_audit()
    {
        var user = _identity.AddUser(UserEmail);
        _identity.LinkExternalLogin(user.Id, "Google", "google-123");
        _identity.PendingExternalLogin = GoogleLogin();

        var result = await Service().SignInAsync(new ExternalSignInRequest(ReturnUrl), Ct);

        Assert.Equal(ReturnUrl, result.Value.ReturnUrl);
        Assert.Equal([user.Id], _identity.SignedInUsers);
        Assert.True(_identity.ExternalSignedOut);
        Assert.Equal(1, _unitOfWork.SaveChangesCalls);
        Assert.True(Assert.Single(_audits.Audits).Succeeded);
    }

    [Fact]
    public async Task New_account_is_created_and_linked_through_the_repository()
    {
        _identity.PendingExternalLogin = GoogleLogin();

        var result = await Service().SignInAsync(new ExternalSignInRequest(ReturnUrl), Ct);

        Assert.True(result.IsSuccess);
        var user = Assert.Single(_identity.Users);
        Assert.Equal("Ana Pérez", user.DisplayName);
        Assert.Equal(user.Id, (await _identity.FindByExternalLoginAsync("Google", "google-123", Ct))?.Id);
        Assert.Equal(1, _unitOfWork.SaveChangesCalls);
    }

    [Fact]
    public async Task Existing_unverified_account_is_confirmed_when_google_email_is_verified()
    {
        var user = await _identity.CreateUnverifiedAsync(
            Domain.ValueObjects.Email.Create(UserEmail).Value, null, "Ana", "es", Ct);
        _identity.PendingExternalLogin = GoogleLogin();

        var result = await Service().SignInAsync(new ExternalSignInRequest(ReturnUrl), Ct);

        Assert.True(result.IsSuccess);
        Assert.True(Assert.Single(_identity.Users).EmailConfirmed);
        Assert.Equal(user.Id, (await _identity.FindByExternalLoginAsync("Google", "google-123", Ct))?.Id);
    }

    [Fact]
    public async Task Unverified_google_email_fails_and_persists_the_audit()
    {
        _identity.AddUser(UserEmail);
        _identity.PendingExternalLogin = GoogleLogin(verified: false);

        var result = await Service().SignInAsync(new ExternalSignInRequest(ReturnUrl), Ct);

        Assert.Equal(ExternalLoginErrors.EmailNotVerifiedCode, result.Error.Code);
        Assert.Empty(_identity.SignedInUsers);
        Assert.True(_identity.ExternalSignedOut);
        Assert.Equal(1, _unitOfWork.SaveChangesCalls);
        Assert.False(Assert.Single(_audits.Audits).Succeeded);
    }

    [Fact]
    public async Task Missing_external_cookie_fails_and_persists_the_audit()
    {
        var result = await Service().SignInAsync(new ExternalSignInRequest(ReturnUrl), Ct);

        Assert.Equal(ExternalLoginErrors.FailedCode, result.Error.Code);
        Assert.False(_identity.ExternalSignedOut);
        Assert.Equal(1, _unitOfWork.SaveChangesCalls);
        Assert.False(Assert.Single(_audits.Audits).Succeeded);
    }

    [Fact]
    public async Task Invite_only_rejects_unknown_account_and_persists_the_audit()
    {
        _settings.Mode = RegistrationMode.InviteOnly;
        _identity.PendingExternalLogin = GoogleLogin();

        var result = await Service().SignInAsync(new ExternalSignInRequest(ReturnUrl), Ct);

        Assert.Equal(AccountErrors.NotInvitedCode, result.Error.Code);
        Assert.Empty(_identity.Users);
        Assert.Equal(UserEmail, Assert.Single(_audits.Audits).Identifier);
        Assert.Equal(1, _unitOfWork.SaveChangesCalls);
    }

    [Fact]
    public async Task Disabled_or_locked_account_cannot_sign_in()
    {
        var user = _identity.AddUser(UserEmail, isActive: false);
        _identity.LinkExternalLogin(user.Id, "Google", "google-123");
        _identity.PendingExternalLogin = GoogleLogin();

        var disabled = await Service().SignInAsync(new ExternalSignInRequest(ReturnUrl), Ct);
        Assert.Equal(AccountErrors.DisabledCode, disabled.Error.Code);

        await _identity.SetActiveAsync(user.Id, true, Ct);
        _identity.LockedOutUsers.Add(user.Id);
        var locked = await Service().SignInAsync(new ExternalSignInRequest(ReturnUrl), Ct);

        Assert.Equal(AccountErrors.LockedOutCode, locked.Error.Code);
        Assert.Empty(_identity.SignedInUsers);
        Assert.Equal(2, _unitOfWork.SaveChangesCalls);
        Assert.Equal(2, _audits.Audits.Count);
    }

    [Fact]
    public async Task Invalid_return_url_does_not_read_cookie_audit_or_save()
    {
        _identity.PendingExternalLogin = GoogleLogin();

        var result = await Service().SignInAsync(new ExternalSignInRequest("https://attacker.example"), Ct);

        Assert.True(result.IsFailure);
        Assert.False(_identity.ExternalSignedOut);
        Assert.Empty(_identity.Users);
        Assert.Empty(_audits.Audits);
        Assert.Equal(0, _unitOfWork.SaveChangesCalls);
    }

    [Fact]
    public void Request_does_not_expose_oidc_parameters_in_logs()
    {
        var request = new ExternalSignInRequest(ReturnUrl + "&state=secret");

        Assert.Equal(nameof(ExternalSignInRequest), request.ToString());
    }

    private ExternalLoginService Service() => new(
        _identity,
        _identity,
        _identity,
        _audits,
        new AccountCreationPolicy(_settings, new FakeInitialAdmin()),
        new FakeRequestInfo(),
        _time,
        new ServiceRequestValidator<ExternalSignInRequest>([new ExternalSignInRequestValidator()]),
        _unitOfWork,
        NullLogger<ExternalLoginService>.Instance);

    private static ExternalLogin GoogleLogin(bool verified = true) =>
        new("Google", "google-123", "Ana@Example.com", verified, "Ana Pérez");
}
