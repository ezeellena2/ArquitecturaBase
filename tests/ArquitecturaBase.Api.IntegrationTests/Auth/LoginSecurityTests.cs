using System.Net;
using ArquitecturaBase.Api.IntegrationTests.Support;
using ArquitecturaBase.Application.Interfaces.Integrations;
using ArquitecturaBase.Domain.Authentication;
using ArquitecturaBase.Domain.ValueObjects;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace ArquitecturaBase.Api.IntegrationTests.Auth;

[Collection(ApiTestGroup.Name)]
public sealed class LoginSecurityTests(ApiFactory factory)
{
    private const string ReturnUrl = "/connect/authorize";
    private const string UserAgent = "ArquitecturaBase.Tests/1.0";

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Every_attempt_is_audited_with_the_client_and_without_the_code()
    {
        using var client = factory.CreateClient();
        client.DefaultRequestHeaders.UserAgent.ParseAdd(UserAgent);
        var email = TestEmails.Unique("audit");
        var code = await client.RequestCodeAsync(factory, email);

        using var wrong = await client.PostJsonAsync("/account/login-code/verify", new { email, code = WrongCode(code), returnUrl = ReturnUrl });
        using var right = await client.PostJsonAsync("/account/login-code/verify", new { email, code, returnUrl = ReturnUrl });

        var audits = await factory.ExecuteDbContextAsync(db => db.LoginAudits
            .Where(audit => audit.Identifier == email)
            .OrderBy(audit => audit.Succeeded)
            .ToListAsync(Ct));

        Assert.Collection(
            audits,
            failure =>
            {
                Assert.False(failure.Succeeded);
                Assert.Equal(LoginCodeErrors.InvalidCode, failure.FailureReason);
                Assert.Null(failure.UserId);
            },
            success =>
            {
                Assert.True(success.Succeeded);
                Assert.NotNull(success.UserId);
                Assert.Equal(LoginMethod.Code, success.Method);
            });
        Assert.All(audits, audit =>
        {
            Assert.Equal(UserAgent, audit.UserAgent);
            Assert.DoesNotContain(code, audit.FailureReason ?? string.Empty, StringComparison.Ordinal);
        });
    }

    [Fact]
    public async Task Ten_failed_verifications_in_a_row_lock_the_account()
    {
        using var client = factory.CreateClient();
        var email = TestEmails.Unique("lockout");
        await client.SignInWithCodeAsync(factory, email);

        // Cada código admite 5 intentos: hacen falta dos códigos para llegar a 10 fallos seguidos.
        for (var round = 0; round < 2; round++)
        {
            var code = await client.RequestCodeAsync(factory, email);

            for (var attempt = 0; attempt < 5; attempt++)
            {
                using var failed = await client.PostJsonAsync(
                    "/account/login-code/verify", new { email, code = WrongCode(code), returnUrl = ReturnUrl });
                Assert.NotEqual(HttpStatusCode.OK, failed.StatusCode);
            }
        }

        var lastCode = await client.RequestCodeAsync(factory, email);
        using var locked = await client.PostJsonAsync("/account/login-code/verify", new { email, code = lastCode, returnUrl = ReturnUrl }, language: "es");
        var problem = await locked.ReadJsonAsync();

        Assert.Equal(HttpStatusCode.TooManyRequests, locked.StatusCode);
        Assert.Equal(AccountErrors.LockedOutCode, problem.GetProperty("code").GetString());
    }

    [Fact]
    public async Task Disabled_account_is_reported_after_verifying_the_code()
    {
        var email = TestEmails.Unique("disabled");
        using (var first = factory.CreateClient())
        {
            await first.SignInWithCodeAsync(factory, email);
        }

        await DisableAsync(email);
        using var client = factory.CreateClient();
        var code = await client.RequestCodeAsync(factory, email);

        using var response = await client.PostJsonAsync("/account/login-code/verify", new { email, code, returnUrl = ReturnUrl }, language: "es");
        var problem = await response.ReadJsonAsync();

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Equal(AccountErrors.DisabledCode, problem.GetProperty("code").GetString());
        Assert.Equal("Tu cuenta está deshabilitada. Contactá a un administrador.", problem.GetProperty("detail").GetString());
        Assert.False(response.Headers.TryGetValues("Set-Cookie", out var cookies)
            && cookies.Any(cookie => cookie.StartsWith(".AspNetCore.Identity.Application=", StringComparison.Ordinal)));
    }

    /// <summary>
    /// Con el código correcto, una cuenta inactiva queda con el correo confirmado y el código gastado, y responde
    /// Auth.Account.Disabled con su auditoría: el error del verify también se confirma, y el rechazo deja su rastro.
    /// </summary>
    [Fact]
    public async Task An_inactive_account_confirms_its_email_with_a_valid_code_and_still_answers_disabled()
    {
        var email = TestEmails.Unique("inactive-confirms");
        var account = await factory.InTransactionAsync(services => services.GetRequiredService<IIdentityService>()
            .CreateUnverifiedAsync(Email.Create(email).Value, phone: null, "Ana", "es", Ct));
        Assert.False(account.EmailConfirmed);
        await DisableAsync(email);
        using var client = factory.CreateClient();
        var code = await client.RequestCodeAsync(factory, email);

        using var response = await client.PostJsonAsync(
            "/account/login-code/verify", new { email, code, returnUrl = ReturnUrl }, language: "es");

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Equal(AccountErrors.DisabledCode, (await response.ReadJsonAsync()).GetProperty("code").GetString());
        var persisted = await factory.ExecuteDbContextAsync(async db =>
            (EmailConfirmed: await db.Users.Where(user => user.Email == email).Select(user => user.EmailConfirmed).SingleAsync(Ct),
             CodeConsumed: await db.LoginCodes.Where(stored => stored.Destination == email).AnyAsync(stored => stored.ConsumedAtUtc != null, Ct),
             Audit: await db.LoginAudits.Where(audit => audit.Identifier == email).Select(audit => audit.FailureReason).SingleAsync(Ct)));
        Assert.True(persisted.EmailConfirmed);
        Assert.True(persisted.CodeConsumed);
        Assert.Equal(AccountErrors.DisabledCode, persisted.Audit);
    }

    [Fact]
    public async Task Disabled_user_cannot_refresh_tokens_or_reuse_the_session()
    {
        using var client = factory.CreateClient();
        var email = TestEmails.Unique("disabledtokens");
        var tokens = await client.LoginAsync(factory, email);

        await DisableAsync(email);
        using var refresh = await client.RefreshAsync(tokens.RefreshToken);
        using var authorize = await client.AuthorizeAsync(Pkce.ChallengeOf(Pkce.CreateVerifier()));

        Assert.Equal(HttpStatusCode.BadRequest, refresh.StatusCode);
        Assert.Equal("invalid_grant", (await refresh.ReadJsonAsync()).GetProperty("error").GetString());
        Assert.StartsWith("/login?", authorize.Headers.Location!.OriginalString, StringComparison.Ordinal);
    }

    private static string WrongCode(string code) => code == "000000" ? "111111" : "000000";

    // Con el change tracker (no ExecuteUpdate): así pasa por el interceptor de auditoría, como en producción.
    private Task<int> DisableAsync(string email) =>
        factory.ExecuteDbContextAsync(async db =>
        {
            var user = await db.Users.SingleAsync(u => u.Email == email, Ct);
            user.IsActive = false;
            return await db.SaveChangesAsync(Ct);
        });
}
