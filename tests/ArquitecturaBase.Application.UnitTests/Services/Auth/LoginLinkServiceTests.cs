using ArquitecturaBase.Application.Common.Validation;
using ArquitecturaBase.Application.Interfaces.Persistence;
using ArquitecturaBase.Application.Models.Auth;
using ArquitecturaBase.Application.Services.Auth;
using ArquitecturaBase.Application.UnitTests.TestDoubles;
using ArquitecturaBase.Application.UnitTests.TestDoubles.Auth;
using ArquitecturaBase.Application.UnitTests.TestDoubles.Users;
using ArquitecturaBase.Application.Validation.Auth;
using ArquitecturaBase.Domain.Authentication;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;

namespace ArquitecturaBase.Application.UnitTests.Services.Auth;

/// <summary>
/// El canje del enlace de ingreso (LoginLinkService.RedeemAsync): un límite con OnAnyResult, y la cookie de la aplicación
/// recién después del commit, como en los otros dos ingresos. Lo que se ve por HTTP lo fija LoginLinkTests.
/// </summary>
public sealed class LoginLinkServiceTests
{
    /// <summary>Un token con la forma de los de verdad: 43 caracteres de base64url.</summary>
    private const string Token = "AnaLoginLinkToken0123456789abcdefghijklmnop";

    private const string Phone = "+5493511234567";

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Redeeming_signs_in_after_the_commit()
    {
        var fixture = new Fixture();
        var user = fixture.Accounts.AddUser(email: null, phoneNumber: Phone);
        fixture.IssueLink(user.Id);

        var result = await fixture.Service.RedeemAsync(new RedeemLoginLinkRequest(Token), Ct);

        Assert.True(result.IsSuccess);
        Assert.Equal(["commit", "sign-in"], fixture.Events);
        Assert.Equal([user.Id], fixture.SignIn.SignedInUsers);
        Assert.Equal(1, fixture.UnitOfWork.Commits);
        Assert.Equal(CommitPolicy.OnAnyResult, fixture.UnitOfWork.LastPolicy);
        Assert.NotNull(Assert.Single(fixture.Links.Links).ConsumedAtUtc);
        Assert.True(Assert.Single(fixture.Audits.Audits).Succeeded);
    }

    [Fact]
    public async Task Failed_commit_does_not_issue_the_application_cookie()
    {
        var fixture = new Fixture();
        var user = fixture.Accounts.AddUser(email: null, phoneNumber: Phone);
        fixture.IssueLink(user.Id);
        fixture.UnitOfWork.CommitFailure = new InvalidOperationException("commit failed");

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(
            () => fixture.Service.RedeemAsync(new RedeemLoginLinkRequest(Token), Ct));

        Assert.Equal("commit failed", exception.Message);
        Assert.Equal(["commit"], fixture.Events);
        Assert.Empty(fixture.SignIn.SignedInUsers);
    }

    /// <summary>
    /// Si la cookie falla después del commit, el canje ya quedó confirmado: el enlace queda gastado, la auditoría de éxito
    /// se queda y la excepción sale (un 500). La persona pide otro enlace.
    /// </summary>
    [Fact]
    public async Task A_cookie_failure_after_the_commit_leaves_the_link_spent_and_the_success_audited()
    {
        var fixture = new Fixture();
        var user = fixture.Accounts.AddUser(email: null, phoneNumber: Phone);
        fixture.IssueLink(user.Id);
        fixture.SignIn.SignInFailure = new InvalidOperationException("cookie failed");

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(
            () => fixture.Service.RedeemAsync(new RedeemLoginLinkRequest(Token), Ct));

        Assert.Equal("cookie failed", exception.Message);
        Assert.Equal(["commit", "sign-in"], fixture.Events);
        Assert.Equal(1, fixture.UnitOfWork.Commits);
        Assert.Equal(0, fixture.UnitOfWork.Rollbacks);
        Assert.Empty(fixture.SignIn.SignedInUsers);
        Assert.NotNull(Assert.Single(fixture.Links.Links).ConsumedAtUtc);
        Assert.True(Assert.Single(fixture.Audits.Audits).Succeeded);
    }

    /// <summary>El enlace se gasta y queda la auditoría, pero sin cookie: el error también se confirma.</summary>
    [Theory]
    [InlineData("locked out")]
    [InlineData("disabled")]
    public async Task Locked_or_disabled_accounts_spend_the_link_without_the_cookie(string state)
    {
        var fixture = new Fixture();
        var user = fixture.Accounts.AddUser(email: null, isActive: state != "disabled", phoneNumber: Phone);
        if (state == "locked out")
        {
            fixture.SignIn.LockedOutUsers.Add(user.Id);
        }

        fixture.IssueLink(user.Id);

        var result = await fixture.Service.RedeemAsync(new RedeemLoginLinkRequest(Token), Ct);

        Assert.Equal(state == "disabled" ? AccountErrors.DisabledCode : AccountErrors.LockedOutCode, result.Error.Code);
        Assert.Equal(["commit"], fixture.Events);
        Assert.Empty(fixture.SignIn.SignedInUsers);
        Assert.NotNull(Assert.Single(fixture.Links.Links).ConsumedAtUtc);
        Assert.False(Assert.Single(fixture.Audits.Audits).Succeeded);
    }

    private sealed class Fixture
    {
        public Fixture()
        {
            UnitOfWork = new FakeUnitOfWork(Events);
            SignIn = new FakeSignInService(Events);
            Links.InTransaction = () => UnitOfWork.InTransaction;
            Accounts.InTransaction = () => UnitOfWork.InTransaction;
            SignIn.InTransaction = () => UnitOfWork.InTransaction;
            Service = new LoginLinkService(
                Links,
                Audits,
                new FakeSecureTokenGenerator(),
                Accounts,
                SignIn,
                new FakePhoneNumberParser(),
                new FakeRequestInfo(),
                Clock,
                new ServiceRequestValidator<PreviewLoginLinkRequest>([new PreviewLoginLinkRequestValidator()]),
                new ServiceRequestValidator<RedeemLoginLinkRequest>([new RedeemLoginLinkRequestValidator()]),
                UnitOfWork,
                NullLogger<LoginLinkService>.Instance);
        }

        /// <summary>"commit" (FakeUnitOfWork) y "sign-in" (FakeSignInService), en el orden en que pasaron.</summary>
        public List<string> Events { get; } = [];

        public FakeTimeProvider Clock { get; } = new(new DateTimeOffset(2026, 9, 27, 12, 0, 0, TimeSpan.Zero));

        public InMemoryLoginLinkRepository Links { get; } = new();

        public InMemoryLoginAuditRepository Audits { get; } = new();

        public InMemoryUserAccounts Accounts { get; } = new();

        public FakeSignInService SignIn { get; }

        public FakeUnitOfWork UnitOfWork { get; }

        public LoginLinkService Service { get; }

        public void IssueLink(Guid userId) =>
            Links.Add(LoginLink.Issue(userId, FakeSecureTokenGenerator.HashOf(Token), Clock.GetUtcNow().UtcDateTime));
    }
}
