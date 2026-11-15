using System.Globalization;
using System.Net;
using ArquitecturaBase.Api.IntegrationTests.Support;
using ArquitecturaBase.Application.Interfaces.Persistence;
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

        // Cada código admite 5 intentos: hacen falta dos códigos para llegar a 10 fallos seguidos. Sólo suma un fallo
        // contra el código activo (N3), y el reloj del arnés no avanza solo: se mueve para que cada código sea el más
        // nuevo sin empatar con el anterior.
        for (var round = 0; round < 2; round++)
        {
            factory.Clock.Advance(TimeSpan.FromSeconds(1));
            var code = await client.RequestCodeAsync(factory, email);

            for (var attempt = 0; attempt < 5; attempt++)
            {
                using var failed = await client.PostJsonAsync(
                    "/account/login-code/verify", new { email, code = WrongCode(code), returnUrl = ReturnUrl });
                Assert.NotEqual(HttpStatusCode.OK, failed.StatusCode);
            }
        }

        factory.Clock.Advance(TimeSpan.FromSeconds(1));
        var lastCode = await client.RequestCodeAsync(factory, email);
        using var locked = await client.PostJsonAsync("/account/login-code/verify", new { email, code = lastCode, returnUrl = ReturnUrl }, language: "es");
        var problem = await locked.ReadJsonAsync();

        Assert.Equal(HttpStatusCode.TooManyRequests, locked.StatusCode);
        Assert.Equal(AccountErrors.LockedOutCode, problem.GetProperty("code").GetString());
    }

    /// <summary>
    /// N3: quien conoce el correo de otro y manda verificaciones con un código cualquiera, sin haber pedido ninguno, no le
    /// bloquea la cuenta: sólo suma un código equivocado contra uno activo. Su dueño entra después con su código.
    /// </summary>
    [Fact]
    public async Task Verifications_without_a_code_do_not_lock_the_account()
    {
        var email = TestEmails.Unique("nolock");
        using (var owner = factory.CreateClient())
        {
            await owner.SignInWithCodeAsync(factory, email);
        }

        using var attacker = factory.CreateClient();
        for (var attempt = 0; attempt < 12; attempt++)
        {
            using var failed = await attacker.PostJsonAsync(
                "/account/login-code/verify", new { email, code = (100000 + attempt).ToString(CultureInfo.InvariantCulture), returnUrl = ReturnUrl });
            Assert.Equal(HttpStatusCode.BadRequest, failed.StatusCode);
        }

        using var client = factory.CreateClient();
        var code = await client.RequestCodeAsync(factory, email);
        using var right = await client.PostJsonAsync("/account/login-code/verify", new { email, code, returnUrl = ReturnUrl });

        Assert.Equal(HttpStatusCode.OK, right.StatusCode);
    }

    /// <summary>
    /// N3: un código cualquiera responde lo mismo para un correo sin cuenta, para una cuenta bloqueada y para una cuyo
    /// último código ya se usó o venció: la respuesta no dice si la cuenta existe. Usado, vencido y bloqueado se dicen
    /// sólo a quien manda el código correcto.
    /// </summary>
    [Fact]
    public async Task A_random_code_does_not_reveal_whether_the_account_exists()
    {
        using var client = factory.CreateClient();
        var withoutAccount = TestEmails.Unique("random-none");
        var locked = TestEmails.Unique("random-locked");
        var used = TestEmails.Unique("random-used");
        var expired = TestEmails.Unique("random-expired");

        string[] realCodes =
        [
            await SignInReturningTheCodeAsync(client, locked),
            await SignInReturningTheCodeAsync(client, used),
            await SignInReturningTheCodeAsync(client, expired),
            await client.RequestCodeAsync(factory, expired),
        ];
        await LockAsync(locked);
        factory.Clock.Advance(TimeSpan.FromMinutes(10));
        // Uno cualquiera, que no sea ninguno de los que salieron.
        var randomCode = Enumerable.Range(1, realCodes.Length + 1)
            .Select(candidate => candidate.ToString("D6", CultureInfo.InvariantCulture))
            .First(candidate => !realCodes.Contains(candidate));

        List<(HttpStatusCode Status, string? Code, string? Detail, bool HasAttemptsLeft)> answers = [];
        foreach (var email in new[] { withoutAccount, locked, used, expired })
        {
            using var response = await client.PostJsonAsync(
                "/account/login-code/verify", new { email, code = randomCode, returnUrl = ReturnUrl }, language: "es");
            var problem = await response.ReadJsonAsync();
            answers.Add((response.StatusCode, problem.GetProperty("code").GetString(),
                problem.GetProperty("detail").GetString(), problem.TryGetProperty(LoginCodeErrors.AttemptsLeftKey, out _)));
        }

        Assert.All(answers, answer => Assert.Equal(answers[0], answer));
        Assert.Equal((HttpStatusCode.BadRequest, LoginCodeErrors.InvalidCode), (answers[0].Status, answers[0].Code));
        Assert.False(answers[0].HasAttemptsLeft);
    }

    [Fact]
    public async Task Account_disabled_after_requesting_the_code_is_rejected_when_verifying_it()
    {
        var email = TestEmails.Unique("disabled");
        using (var first = factory.CreateClient())
        {
            await first.SignInWithCodeAsync(factory, email);
        }

        using var client = factory.CreateClient();
        var code = await client.RequestCodeAsync(factory, email);
        await DisableAsync(email);

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
        var account = await factory.InTransactionAsync(services => services.GetRequiredService<IUserRepository>()
            .CreateUnverifiedAsync(Email.Create(email).Value, phone: null, "Ana", "es", Ct));
        Assert.False(account.EmailConfirmed);
        using var client = factory.CreateClient();
        var code = await client.RequestCodeAsync(factory, email);
        await DisableAsync(email);

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

    private async Task<string> SignInReturningTheCodeAsync(HttpClient client, string email)
    {
        var code = await client.RequestCodeAsync(factory, email);
        using var response = await client.PostJsonAsync("/account/login-code/verify", new { email, code, returnUrl = ReturnUrl });
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        return code;
    }

    private Task<int> LockAsync(string email) =>
        factory.ExecuteDbContextAsync(async db =>
        {
            var user = await db.Users.SingleAsync(u => u.Email == email, Ct);
            user.LockoutEnd = factory.Clock.GetUtcNow().AddHours(1);
            return await db.SaveChangesAsync(Ct);
        });

    // Con el change tracker (no ExecuteUpdate): así pasa por el interceptor de auditoría, como en producción.
    private Task<int> DisableAsync(string email) =>
        factory.ExecuteDbContextAsync(async db =>
        {
            var user = await db.Users.SingleAsync(u => u.Email == email, Ct);
            user.SetActive(false);
            return await db.SaveChangesAsync(Ct);
        });
}
