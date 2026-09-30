using ArquitecturaBase.Application.Interfaces.Persistence;
using ArquitecturaBase.Application.Services.Auth;
using ArquitecturaBase.Application.Models.Auth;
using ArquitecturaBase.Application.Models.Identity;
using ArquitecturaBase.Application.UnitTests.TestDoubles;
using ArquitecturaBase.Application.UnitTests.TestDoubles.Auth;
using ArquitecturaBase.Application.UnitTests.TestDoubles.Users;
using ArquitecturaBase.Application.Validation.Auth;
using ArquitecturaBase.Domain.Authentication;
using ArquitecturaBase.Domain.Settings;
using ArquitecturaBase.Domain.Users;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;

namespace ArquitecturaBase.Application.UnitTests.Services.Auth;

/// <summary>
/// El ingreso con Google (ExternalLoginService.SignInAsync): un límite con OnAnyResult, y la cookie de la aplicación
/// recién después del commit, como en los otros dos ingresos. Lo que se ve por HTTP lo fija ExternalLoginTests.
/// </summary>
public sealed class ExternalLoginServiceTests
{
    private const string ReturnUrl = "/connect/authorize?client_id=web";
    private const string UserEmail = "ana@example.com";

    /// <summary>"commit" (FakeUnitOfWork) y "sign-in" (FakeSignInService), en el orden en que pasaron.</summary>
    private readonly List<string> _events = [];
    private readonly InMemoryUserAccounts _accounts = new();
    private readonly FakeSignInService _signIn;
    private readonly InMemoryLoginAuditRepository _audits = new();
    private readonly FakeSystemSettingsReader _settings = new();
    private readonly FakeUnitOfWork _unitOfWork;
    private readonly FakeTimeProvider _time = new(new DateTimeOffset(2026, 9, 19, 12, 0, 0, TimeSpan.Zero));

    public ExternalLoginServiceTests()
    {
        _unitOfWork = new FakeUnitOfWork(_events);
        _signIn = new FakeSignInService(_events);
        _accounts.InTransaction = () => _unitOfWork.InTransaction;
        _signIn.InTransaction = () => _unitOfWork.InTransaction;
    }

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Linked_account_signs_in_after_the_commit_and_persists_a_successful_audit()
    {
        var user = _accounts.AddUser(UserEmail);
        _accounts.LinkExternalLogin(user.Id, "Google", "google-123");
        _signIn.PendingExternalLogin = GoogleLogin();

        var result = await Service().SignInAsync(new ExternalSignInRequest(ReturnUrl), Ct);

        Assert.Equal(ReturnUrl, result.Value.ReturnUrl);
        Assert.Equal(["commit", "sign-in"], _events);
        Assert.Equal([user.Id], _signIn.SignedInUsers);
        Assert.True(_signIn.ExternalSignedOut);
        Assert.Equal(1, _unitOfWork.Commits);
        Assert.Equal(CommitPolicy.OnAnyResult, _unitOfWork.LastPolicy);
        Assert.True(Assert.Single(_audits.Audits).Succeeded);
    }

    /// <summary>
    /// Entrar bien con Google pone en cero los intentos fallidos, igual que el código y el enlace. La llamada va adentro
    /// del límite: FakeSignInService lanza si se la hace afuera.
    /// </summary>
    [Fact]
    public async Task Google_sign_in_resets_the_failed_attempts()
    {
        var user = _accounts.AddUser(UserEmail);
        _accounts.LinkExternalLogin(user.Id, "Google", "google-123");
        _signIn.FailedAttempts[user.Id] = 3;
        _signIn.PendingExternalLogin = GoogleLogin();

        var result = await Service().SignInAsync(new ExternalSignInRequest(ReturnUrl), Ct);

        Assert.True(result.IsSuccess);
        Assert.Equal(0, _signIn.FailedAttempts[user.Id]);
        Assert.Equal(["commit", "sign-in"], _events);
    }

    [Fact]
    public async Task New_account_is_created_and_linked_through_the_repository()
    {
        _signIn.PendingExternalLogin = GoogleLogin();

        var result = await Service().SignInAsync(new ExternalSignInRequest(ReturnUrl), Ct);

        Assert.True(result.IsSuccess);
        var user = Assert.Single(_accounts.Users);
        Assert.Equal("Ana Pérez", user.DisplayName);
        Assert.Equal(user.Id, (await _accounts.FindByExternalLoginAsync("Google", "google-123", Ct))?.Id);
        Assert.Equal(1, _unitOfWork.Commits);
    }

    /// <summary>
    /// El nombre que manda Google nadie lo tipea: se recorta al tope de la columna (y se limpia el \0) en lugar de
    /// rechazar el ingreso. Los nombres que se tipean los valida HTTP.
    /// </summary>
    [Fact]
    public async Task A_long_Google_name_is_cut_to_the_limit()
    {
        _signIn.PendingExternalLogin = GoogleLogin() with { DisplayName = "Ana\0 " + new string('A', 150) };

        var result = await Service().SignInAsync(new ExternalSignInRequest(ReturnUrl), Ct);

        Assert.True(result.IsSuccess);
        Assert.Equal(
            "Ana " + new string('A', AccountRules.DisplayNameMaxLength - 4),
            Assert.Single(_accounts.Users).DisplayName);
    }

    [Fact]
    public async Task Failed_commit_does_not_issue_the_application_cookie()
    {
        _signIn.PendingExternalLogin = GoogleLogin();
        _unitOfWork.CommitFailure = new ExpectedCommitFailure();

        await Assert.ThrowsAsync<ExpectedCommitFailure>(() =>
            Service().SignInAsync(new ExternalSignInRequest(ReturnUrl), Ct));

        Assert.Equal(["commit"], _events);
        Assert.True(_signIn.ExternalSignedOut);
        Assert.Empty(_signIn.SignedInUsers);
        Assert.Equal(1, _unitOfWork.Rollbacks);
    }

    /// <summary>
    /// Si la cookie falla después del commit, el ingreso ya quedó confirmado: la cuenta nueva, el vínculo con Google y la
    /// auditoría de éxito se quedan, y la excepción sale (un 500). La persona vuelve a entrar con Google.
    /// </summary>
    [Fact]
    public async Task A_cookie_failure_after_the_commit_keeps_the_account_the_link_and_the_success_audit()
    {
        _signIn.PendingExternalLogin = GoogleLogin();
        _signIn.SignInFailure = new ExpectedSignInFailure();

        await Assert.ThrowsAsync<ExpectedSignInFailure>(() =>
            Service().SignInAsync(new ExternalSignInRequest(ReturnUrl), Ct));

        Assert.Equal(["commit", "sign-in"], _events);
        Assert.Equal(1, _unitOfWork.Commits);
        Assert.Equal(0, _unitOfWork.Rollbacks);
        Assert.Empty(_signIn.SignedInUsers);
        var user = Assert.Single(_accounts.Users);
        Assert.Equal(user.Id, (await _accounts.FindByExternalLoginAsync("Google", "google-123", Ct))?.Id);
        Assert.True(Assert.Single(_audits.Audits).Succeeded);
    }

    [Fact]
    public async Task Existing_unverified_account_is_confirmed_when_google_email_is_verified()
    {
        var user = await _accounts.ArrangeAsync(accounts => accounts.CreateUnverifiedAsync(
            Domain.ValueObjects.Email.Create(UserEmail).Value, null, "Ana", "es", Ct));
        _signIn.PendingExternalLogin = GoogleLogin();

        var result = await Service().SignInAsync(new ExternalSignInRequest(ReturnUrl), Ct);

        Assert.True(result.IsSuccess);
        Assert.True(Assert.Single(_accounts.Users).EmailConfirmed);
        Assert.Equal(user.Id, (await _accounts.FindByExternalLoginAsync("Google", "google-123", Ct))?.Id);
    }

    [Fact]
    public async Task Unverified_google_email_fails_and_persists_the_audit()
    {
        _accounts.AddUser(UserEmail);
        _signIn.PendingExternalLogin = GoogleLogin(verified: false);

        var result = await Service().SignInAsync(new ExternalSignInRequest(ReturnUrl), Ct);

        Assert.Equal(ExternalLoginErrors.EmailNotVerifiedCode, result.Error.Code);
        Assert.Empty(_signIn.SignedInUsers);
        Assert.True(_signIn.ExternalSignedOut);
        Assert.Equal(1, _unitOfWork.Commits);
        Assert.Equal(CommitPolicy.OnAnyResult, _unitOfWork.LastPolicy);
        Assert.False(Assert.Single(_audits.Audits).Succeeded);
    }

    [Fact]
    public async Task Missing_external_cookie_fails_and_persists_the_audit()
    {
        var result = await Service().SignInAsync(new ExternalSignInRequest(ReturnUrl), Ct);

        Assert.Equal(ExternalLoginErrors.FailedCode, result.Error.Code);
        Assert.False(_signIn.ExternalSignedOut);
        Assert.Equal(1, _unitOfWork.Commits);
        Assert.Equal(CommitPolicy.OnAnyResult, _unitOfWork.LastPolicy);
        Assert.False(Assert.Single(_audits.Audits).Succeeded);
    }

    [Fact]
    public async Task Invite_only_rejects_unknown_account_and_persists_the_audit()
    {
        _settings.Mode = RegistrationMode.InviteOnly;
        _signIn.PendingExternalLogin = GoogleLogin();

        var result = await Service().SignInAsync(new ExternalSignInRequest(ReturnUrl), Ct);

        Assert.Equal(AccountErrors.NotInvitedCode, result.Error.Code);
        Assert.Empty(_accounts.Users);
        Assert.Equal(UserEmail, Assert.Single(_audits.Audits).Identifier);
        Assert.Equal(1, _unitOfWork.Commits);
        Assert.Equal(CommitPolicy.OnAnyResult, _unitOfWork.LastPolicy);
    }

    [Fact]
    public async Task Disabled_or_locked_account_cannot_sign_in()
    {
        var user = _accounts.AddUser(UserEmail, isActive: false);
        _accounts.LinkExternalLogin(user.Id, "Google", "google-123");
        _signIn.FailedAttempts[user.Id] = 3;
        _signIn.PendingExternalLogin = GoogleLogin();

        var disabled = await Service().SignInAsync(new ExternalSignInRequest(ReturnUrl), Ct);
        Assert.Equal(AccountErrors.DisabledCode, disabled.Error.Code);

        await _accounts.ArrangeAsync(accounts => accounts.SetActiveAsync(user.Id, true, Ct));
        _signIn.LockedOutUsers.Add(user.Id);
        var locked = await Service().SignInAsync(new ExternalSignInRequest(ReturnUrl), Ct);

        Assert.Equal(AccountErrors.LockedOutCode, locked.Error.Code);
        Assert.Equal(["commit", "commit"], _events);
        Assert.Empty(_signIn.SignedInUsers);
        Assert.Equal(2, _unitOfWork.Commits);
        Assert.Equal(2, _audits.Audits.Count);
        Assert.Equal(3, _signIn.FailedAttempts[user.Id]);
    }

    [Fact]
    public async Task Invalid_return_url_does_not_read_cookie_audit_or_save()
    {
        _signIn.PendingExternalLogin = GoogleLogin();

        var result = await Service().SignInAsync(new ExternalSignInRequest("https://attacker.example"), Ct);

        Assert.True(result.IsFailure);
        Assert.False(_signIn.ExternalSignedOut);
        Assert.Empty(_accounts.Users);
        Assert.Empty(_audits.Audits);
        Assert.Equal(0, _unitOfWork.Transactions);
    }

    [Fact]
    public async Task Explicit_google_login_does_not_create_an_account()
    {
        _signIn.PendingExternalLogin = GoogleLogin();
        var result = await Service().SignInAsync(new ExternalSignInRequest(ReturnUrl, Register: false), Ct);
        Assert.Equal("Auth.Account.NotRegistered", result.Error.Code);
        Assert.Empty(_accounts.Users);
        Assert.Empty(_signIn.SignedInUsers);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Explicit_google_registration_rejects_existing_accounts(bool linked)
    {
        var user = _accounts.AddUser(UserEmail);
        if (linked) { _accounts.LinkExternalLogin(user.Id, "Google", "google-123"); }
        _signIn.PendingExternalLogin = GoogleLogin();
        var result = await Service().SignInAsync(new ExternalSignInRequest(ReturnUrl, Register: true), Ct);
        Assert.Equal("Auth.Account.AlreadyRegistered", result.Error.Code);
        Assert.Empty(_signIn.SignedInUsers);
        Assert.Single(_accounts.Users);
    }

    private ExternalLoginService Service() => new(
        _signIn,
        _accounts,
        _accounts,
        new LoginAuditRecorder(_audits, new FakeRequestInfo(), _time),
        new AccountCreationPolicy(_settings, new FakeInitialAdmin()),
        RequestValidators.For(new ExternalSignInRequestValidator()),
        _unitOfWork,
        NullLogger<ExternalLoginService>.Instance);

    private sealed class ExpectedCommitFailure : Exception;

    private sealed class ExpectedSignInFailure : Exception;

    private static ExternalLogin GoogleLogin(bool verified = true) =>
        new("Google", "google-123", "Ana@Example.com", verified, "Ana Pérez");
}
