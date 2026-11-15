using System.Globalization;
using System.Net;
using ArquitecturaBase.Api.Contracts.Auth;
using ArquitecturaBase.Api.IntegrationTests.Support;
using ArquitecturaBase.Application.Interfaces.Persistence;
using ArquitecturaBase.Domain.Authentication;
using ArquitecturaBase.Domain.ValueObjects;
using ArquitecturaBase.Infrastructure.Persistence;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authentication.Google;
using Microsoft.AspNetCore.Http.Metadata;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc.Controllers;
using Microsoft.AspNetCore.Routing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Microsoft.Net.Http.Headers;

namespace ArquitecturaBase.Api.IntegrationTests.Auth;

[Collection(ApiTestGroup.Name)]
public sealed class ExternalLoginTests(ApiFactory factory)
{
    private const string ReturnUrl = "/connect/authorize?client_id=web";

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public void Both_google_routes_are_anonymous_mvc_actions_tagged_for_openapi()
    {
        var endpoints = factory.Services.GetRequiredService<EndpointDataSource>().Endpoints
            .OfType<RouteEndpoint>()
            .Where(endpoint => endpoint.RoutePattern.RawText is "account/external/google" or "account/external/callback")
            .ToArray();

        Assert.Equal(2, endpoints.Length);
        foreach (var endpoint in endpoints)
        {
            Assert.Contains("GET", endpoint.Metadata.GetMetadata<IHttpMethodMetadata>()!.HttpMethods);
            Assert.IsType<ControllerActionDescriptor>(endpoint.Metadata.GetMetadata<ControllerActionDescriptor>());
            Assert.NotNull(endpoint.Metadata.GetMetadata<IAllowAnonymous>());
            Assert.Contains("Account", endpoint.Metadata.GetMetadata<ITagsMetadata>()!.Tags);
        }
    }

    [Fact]
    public async Task Google_challenge_goes_to_google_with_the_callback_and_the_provider()
    {
        using var client = factory.CreateClient();

        using var response = await client.SendAsync(HttpMethod.Get, "/account/external/google?returnUrl=" + Uri.EscapeDataString(ReturnUrl));

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        var location = response.Headers.Location!;
        Assert.Equal("https://accounts.google.com/o/oauth2/v2/auth", location.GetLeftPart(UriPartial.Path));

        var query = QueryHelpers.ParseQuery(location.Query);
        Assert.Equal(factory.Services.GetRequiredService<IConfiguration>()["Authentication:Google:ClientId"], query["client_id"].ToString());
        Assert.Equal("https://localhost/signin-google", query["redirect_uri"].ToString());

        // Lo que Google devuelve en el state es lo que después lee GetExternalLoginInfoAsync.
        var properties = factory.Services.GetRequiredService<IOptionsMonitor<GoogleOptions>>()
            .Get(GoogleDefaults.AuthenticationScheme)
            .StateDataFormat.Unprotect(query["state"].ToString());
        Assert.Equal(GoogleDefaults.AuthenticationScheme, properties!.Items["LoginProvider"]);
        Assert.Equal("/account/external/callback?returnUrl=" + Uri.EscapeDataString(ReturnUrl), properties.RedirectUri);
    }

    [Fact]
    public async Task Google_challenge_rejects_a_return_url_outside_the_authorize_endpoint()
    {
        using var client = factory.CreateClient();

        using var response = await client.SendAsync(
            HttpMethod.Get, "/account/external/google?returnUrl=" + Uri.EscapeDataString("https://evil.example/"), language: "es");
        var problem = await response.ReadJsonAsync();

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("La dirección de retorno no es válida.", problem.GetProperty("errors").GetProperty("returnUrl")[0].GetString());
    }

    [Fact]
    public async Task Google_challenge_requires_a_return_url()
    {
        using var client = factory.CreateClient();

        using var response = await client.SendAsync(HttpMethod.Get, "/account/external/google");
        var problem = await response.ReadJsonAsync();

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("Validation.Failed", problem.GetProperty("code").GetString());
        Assert.True(problem.GetProperty("errors").TryGetProperty("returnUrl", out _));
    }

    [Fact]
    public void Return_url_is_not_included_in_mvc_action_argument_logging()
    {
        Assert.Equal(nameof(ExternalLoginQuery),
            new ExternalLoginQuery { ReturnUrl = ReturnUrl + "&state=secret" }.ToString());
    }

    [Fact]
    public async Task Google_challenge_is_not_found_without_a_client_id()
    {
        await using var api = factory.WithWebHostBuilder(builder => builder.UseSetting("Authentication:Google:ClientId", ""));
        using var client = api.CreateClient();

        using var response = await client.SendAsync(HttpMethod.Get, "/account/external/google?returnUrl=" + Uri.EscapeDataString(ReturnUrl));

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Api_does_not_start_without_the_google_client_secret_and_explains_how_to_load_it()
    {
        await using var api = factory.WithWebHostBuilder(builder => builder.UseSetting("Authentication:Google:ClientSecret", ""));

        var exception = Assert.ThrowsAny<Exception>(() => api.Services);

        var messages = new List<string>();
        for (var current = exception; current is not null; current = current.InnerException)
        {
            messages.Add(current.Message);
        }

        Assert.Contains(messages, message =>
            message.Contains("Authentication:Google:ClientSecret", StringComparison.Ordinal)
            && message.Contains("user-secrets", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Google_failure_or_cancellation_goes_back_to_login_with_the_error()
    {
        using var client = factory.CreateClient();

        using var response = await client.SendAsync(HttpMethod.Get, "/signin-google?error=access_denied&state=x");

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Equal("/login?error=" + ExternalLoginErrors.FailedCode, response.Headers.Location!.OriginalString);
    }

    [Fact]
    public async Task Verified_google_account_is_created_linked_signed_in_and_audited()
    {
        using var client = factory.CreateClient();
        var email = TestEmails.Unique("google");
        var providerKey = "google-" + Guid.NewGuid().ToString("N", CultureInfo.InvariantCulture);
        using var external = await client.PostJsonAsync("/test/external-login", new { providerKey, email, name = "Ana Pérez", emailVerified = true });

        using var callback = await client.SendAsync(HttpMethod.Get, "/account/external/callback?returnUrl=" + Uri.EscapeDataString(ReturnUrl));
        using var authorize = await client.AuthorizeAsync(Pkce.ChallengeOf(Pkce.CreateVerifier()));

        Assert.True(external.IsSuccessStatusCode);
        Assert.Equal(HttpStatusCode.Redirect, callback.StatusCode);
        Assert.Equal(ReturnUrl, callback.Headers.Location!.OriginalString);
        Assert.False(string.IsNullOrEmpty(AuthFlow.CodeFromRedirect(authorize)));

        // La cookie externa solo sirve para este paso: el callback la cierra (vence en el pasado).
        var externalCookieName = factory.Services.GetRequiredService<IOptionsMonitor<CookieAuthenticationOptions>>()
            .Get(IdentityConstants.ExternalScheme)
            .Cookie.Name;
        var externalCookie = SetCookieHeaderValue.ParseList(callback.Headers.GetValues(HeaderNames.SetCookie).ToList())
            .Single(cookie => cookie.Name.Equals(externalCookieName, StringComparison.Ordinal));
        Assert.True(externalCookie.Expires < factory.Clock.GetUtcNow());

        var (displayName, loginProvider, audit) = await factory.ExecuteDbContextAsync(async db =>
        {
            var user = await db.Users.SingleAsync(u => u.Email == email, Ct);
            var login = await db.UserLogins.SingleAsync(l => l.UserId == user.Id, Ct);
            var success = await db.LoginAudits.SingleAsync(a => a.Identifier == email && a.Succeeded, Ct);
            return (user.DisplayName, login.LoginProvider, success);
        });
        Assert.Equal("Ana Pérez", displayName);
        Assert.Equal(GoogleDefaults.AuthenticationScheme, loginProvider);
        Assert.Equal(LoginMethod.Google, audit.Method);
    }

    [Fact]
    public async Task Failed_google_commit_rolls_back_autosaved_account_role_link_and_audit()
    {
        var email = TestEmails.Unique("google-rollback");
        var providerKey = "google-" + Guid.NewGuid().ToString("N", CultureInfo.InvariantCulture);
        Guid? createdUserId = null;
        var probe = new CommitFailureProbe
        {
            // Adentro de la transacción, con todo ya guardado: la cuenta, su rol, el vínculo y la auditoría existen.
            BeforeFailing = async (db, ct) =>
            {
                var user = await db.Users.AsNoTracking().SingleAsync(user => user.Email == email, ct);
                createdUserId = user.Id;
                Assert.True(await db.UserRoles.AnyAsync(role => role.UserId == user.Id, ct));
                Assert.True(await db.UserLogins.AnyAsync(login => login.LoginProvider == "Google"
                    && login.ProviderKey == providerKey, ct));
                Assert.True(await db.LoginAudits.AnyAsync(audit => audit.Identifier == email && audit.Succeeded, ct));
            },
        };
        await using var api = factory.WithWebHostBuilder(builder => builder.ConfigureTestServices(services =>
            FailingCommitUnitOfWork.Replace(services, probe)));
        await using (var scope = api.Services.CreateAsyncScope())
        {
            Assert.Equal(factory.ConnectionString,
                scope.ServiceProvider.GetRequiredService<ApplicationDbContext>().Database.GetConnectionString());
        }

        using var client = api.CreateClient();
        using var external = await client.PostJsonAsync(
            "/test/external-login", new { providerKey, email, name = "Ana Pérez", emailVerified = true });
        Assert.True(external.IsSuccessStatusCode);

        using var callback = await client.SendAsync(
            HttpMethod.Get, "/account/external/callback?returnUrl=" + Uri.EscapeDataString(ReturnUrl));

        // Un assert que falle adentro también termina en un 500: RolledBackBeforeLeaving distingue los dos casos.
        Assert.Equal(HttpStatusCode.InternalServerError, callback.StatusCode);
        Assert.True(probe.RolledBackBeforeLeaving);
        var userId = Assert.IsType<Guid>(createdUserId);
        var persisted = await factory.ExecuteDbContextAsync(async db =>
            (User: await db.Users.IgnoreQueryFilters().AnyAsync(user => user.Id == userId, Ct),
             Role: await db.UserRoles.AnyAsync(role => role.UserId == userId, Ct),
             Link: await db.UserLogins.AnyAsync(login => login.LoginProvider == "Google" && login.ProviderKey == providerKey, Ct),
             Audit: await db.LoginAudits.AnyAsync(audit => audit.Identifier == email, Ct)));
        Assert.Equal((false, false, false, false), persisted);
    }

    /// <summary>
    /// Una cuenta que existe por el correo pero está inactiva no entra con Google. Con el correo confirmado queda igual
    /// vinculada a Google, con la auditoría del rechazo: el error también se confirma, y el rechazo deja su rastro. Con el
    /// correo sin confirmar no se vincula ni se confirma nada (N4, 2026-11-15), y responde lo mismo. La sesión no se abre:
    /// la cookie sale solo con un ingreso exitoso.
    /// </summary>
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task An_existing_inactive_account_cannot_enter_with_google_and_is_linked_only_with_its_email_confirmed(
        bool emailConfirmed)
    {
        var email = TestEmails.Unique("google-inactive");
        var providerKey = "google-" + Guid.NewGuid().ToString("N", CultureInfo.InvariantCulture);
        var account = await factory.InTransactionAsync(services => emailConfirmed
            ? services.GetRequiredService<IUserRepository>().CreateAsync(Email.Create(email).Value, displayName: "Ana", "es", Ct)
            : services.GetRequiredService<IUserRepository>()
                .CreateUnverifiedAsync(Email.Create(email).Value, phone: null, "Ana", "es", Ct));
        Assert.Equal(emailConfirmed, account.EmailConfirmed);
        await factory.ExecuteDbContextAsync(async db =>
        {
            var user = await db.Users.SingleAsync(candidate => candidate.Id == account.Id, Ct);
            user.SetActive(false);

            return await db.SaveChangesAsync(Ct);
        });
        using var client = factory.CreateClient();
        using var external = await client.PostJsonAsync(
            "/test/external-login", new { providerKey, email, name = "Ana Pérez", emailVerified = true });
        Assert.True(external.IsSuccessStatusCode);

        using var callback = await client.SendAsync(
            HttpMethod.Get, "/account/external/callback?returnUrl=" + Uri.EscapeDataString(ReturnUrl));

        const string expected = AccountErrors.DisabledCode;
        Assert.Equal(HttpStatusCode.Redirect, callback.StatusCode);
        Assert.Equal("/login?error=" + expected, callback.Headers.Location!.OriginalString);
        Assert.False(callback.Headers.TryGetValues("Set-Cookie", out var cookies)
            && cookies.Any(cookie => cookie.StartsWith(".AspNetCore.Identity.Application=", StringComparison.Ordinal)));
        var persisted = await factory.ExecuteDbContextAsync(async db =>
            (EmailConfirmed: await db.Users.Where(user => user.Id == account.Id).Select(user => user.EmailConfirmed).SingleAsync(Ct),
             Linked: await db.UserLogins.AnyAsync(login => login.UserId == account.Id
                 && login.LoginProvider == "Google" && login.ProviderKey == providerKey, Ct),
             Audit: await db.LoginAudits.Where(audit => audit.UserId == account.Id).Select(audit => audit.FailureReason).SingleAsync(Ct)));
        Assert.Equal(emailConfirmed, persisted.EmailConfirmed);
        Assert.Equal(emailConfirmed, persisted.Linked);
        Assert.Equal(expected, persisted.Audit);
    }

    /// <summary>
    /// N4, decisión del 2026-11-15: la cuenta que da de alta un administrador con un correo (también la que restaura por el
    /// correo) no se vincula a Google por esa dirección hasta que alguien entra con el código al correo. Si no, el dueño
    /// anterior de una dirección que pasó a otra persona (ventas@ de Diego a Nora) entraría con su Google a la cuenta de
    /// Nora. Después del código, Google se vincula por la regla de siempre.
    /// </summary>
    [Fact]
    public async Task An_account_created_by_an_administrator_enters_with_google_only_after_the_email_code()
    {
        using var adminClient = factory.CreateClient();
        var admin = await AdminUsersApi.SignInAsync(factory, adminClient);
        var email = TestEmails.Unique("google-invited");
        var providerKey = "google-" + Guid.NewGuid().ToString("N", CultureInfo.InvariantCulture);
        var nora = await admin.CreateOkAsync(new { email, displayName = "Nora" });

        using var first = factory.CreateClient();
        using (var external = await first.PostJsonAsync(
            "/test/external-login", new { providerKey, email, name = "Diego", emailVerified = true }))
        {
            Assert.True(external.IsSuccessStatusCode);
        }

        using (var rejected = await first.SendAsync(
            HttpMethod.Get, "/account/external/callback?returnUrl=" + Uri.EscapeDataString(ReturnUrl)))
        {
            Assert.Equal(HttpStatusCode.Redirect, rejected.StatusCode);
            Assert.Equal("/login?error=" + ExternalLoginErrors.AccountEmailNotConfirmedCode,
                rejected.Headers.Location!.OriginalString);
            Assert.False(rejected.Headers.TryGetValues("Set-Cookie", out var cookies)
                && cookies.Any(cookie => cookie.StartsWith(".AspNetCore.Identity.Application=", StringComparison.Ordinal)));
        }

        var afterRejection = await factory.ExecuteDbContextAsync(async db =>
            (EmailConfirmed: await db.Users.Where(user => user.Id == nora).Select(user => user.EmailConfirmed).SingleAsync(Ct),
             Linked: await db.UserLogins.AnyAsync(login => login.UserId == nora, Ct),
             Audit: await db.LoginAudits
                 .Where(audit => audit.UserId == nora && audit.Method == LoginMethod.Google)
                 .Select(audit => new { audit.Succeeded, audit.FailureReason })
                 .SingleAsync(Ct)));
        Assert.False(afterRejection.EmailConfirmed);
        Assert.False(afterRejection.Linked);
        Assert.False(afterRejection.Audit.Succeeded);
        Assert.Equal(ExternalLoginErrors.AccountEmailNotConfirmedCode, afterRejection.Audit.FailureReason);

        using var byCode = factory.CreateClient();
        await byCode.SignInWithCodeAsync(factory, email);

        using var second = factory.CreateClient();
        using (var external = await second.PostJsonAsync(
            "/test/external-login", new { providerKey, email, name = "Nora", emailVerified = true }))
        {
            Assert.True(external.IsSuccessStatusCode);
        }

        using var linked = await second.SendAsync(
            HttpMethod.Get, "/account/external/callback?returnUrl=" + Uri.EscapeDataString(ReturnUrl));
        Assert.Equal(HttpStatusCode.Redirect, linked.StatusCode);
        Assert.Equal(ReturnUrl, linked.Headers.Location!.OriginalString);
        Assert.Equal(nora, await factory.ExecuteDbContextAsync(db => db.UserLogins
            .Where(login => login.LoginProvider == GoogleDefaults.AuthenticationScheme && login.ProviderKey == providerKey)
            .Select(login => login.UserId)
            .SingleAsync(Ct)));
    }

    /// <summary>
    /// N3: diez códigos equivocados bloquean el ingreso por código, pero no el de Google: el bloqueo frena a quien prueba
    /// códigos, y si frenara a Google cualquiera le cerraría la entrada a otro mandando códigos a su correo.
    /// </summary>
    [Fact]
    public async Task An_account_locked_by_wrong_codes_still_signs_in_with_google()
    {
        var email = TestEmails.Unique("google-codelocked");
        var providerKey = "google-" + Guid.NewGuid().ToString("N", CultureInfo.InvariantCulture);
        using var attacker = factory.CreateClient();
        await attacker.SignInWithCodeAsync(factory, email);
        for (var round = 0; round < 2; round++)
        {
            // El reloj del arnés no avanza solo: sin moverlo, los códigos quedarían con el mismo CreatedAtUtc y cuál se
            // verifica lo decidiría el plan de Postgres. Sólo suma al bloqueo un fallo contra el código activo.
            factory.Clock.Advance(TimeSpan.FromSeconds(1));
            var code = await attacker.RequestCodeAsync(factory, email);
            for (var attempt = 0; attempt < 5; attempt++)
            {
                using var failed = await attacker.PostJsonAsync("/account/login-code/verify",
                    new { email, code = code == "000000" ? "111111" : "000000", returnUrl = AuthFlow.AuthorizeReturnUrl });
                Assert.Equal(HttpStatusCode.BadRequest, failed.StatusCode);
            }
        }

        var lockedUntil = await factory.ExecuteDbContextAsync(db =>
            db.Users.Where(user => user.Email == email).Select(user => user.LockoutEnd).SingleAsync(Ct));
        Assert.NotNull(lockedUntil);

        using var client = factory.CreateClient();
        using var external = await client.PostJsonAsync(
            "/test/external-login", new { providerKey, email, name = "Ana Pérez", emailVerified = true });
        Assert.True(external.IsSuccessStatusCode);
        using var callback = await client.SendAsync(
            HttpMethod.Get, "/account/external/callback?returnUrl=" + Uri.EscapeDataString(ReturnUrl));

        Assert.Equal(HttpStatusCode.Redirect, callback.StatusCode);
        Assert.Equal(ReturnUrl, callback.Headers.Location!.OriginalString);
        Assert.True(callback.Headers.TryGetValues("Set-Cookie", out var cookies)
            && cookies.Any(cookie => cookie.StartsWith(".AspNetCore.Identity.Application=", StringComparison.Ordinal)));
        Assert.True(await factory.ExecuteDbContextAsync(db => db.LoginAudits
            .AnyAsync(audit => audit.Identifier == email && audit.Method == LoginMethod.Google && audit.Succeeded, Ct)));
    }

    [Fact]
    public async Task Unverified_google_email_goes_back_to_login_with_the_error()
    {
        using var client = factory.CreateClient();
        var email = TestEmails.Unique("unverified");
        using var external = await client.PostJsonAsync(
            "/test/external-login", new { providerKey = "google-" + email, email, name = "Ana", emailVerified = false });

        using var callback = await client.SendAsync(HttpMethod.Get, "/account/external/callback?returnUrl=" + Uri.EscapeDataString(ReturnUrl));

        Assert.Equal(HttpStatusCode.Redirect, callback.StatusCode);
        Assert.Equal("/login?error=" + ExternalLoginErrors.EmailNotVerifiedCode, callback.Headers.Location!.OriginalString);
        Assert.False(await factory.ExecuteDbContextAsync(db => db.Users.AnyAsync(u => u.Email == email, Ct)));
    }

    [Fact]
    public async Task Callback_without_a_google_sign_in_goes_back_to_login()
    {
        using var client = factory.CreateClient();

        using var callback = await client.SendAsync(HttpMethod.Get, "/account/external/callback?returnUrl=" + Uri.EscapeDataString(ReturnUrl));

        Assert.Equal(HttpStatusCode.Redirect, callback.StatusCode);
        Assert.Equal("/login?error=" + ExternalLoginErrors.FailedCode, callback.Headers.Location!.OriginalString);
    }

    [Fact]
    public async Task Callback_with_an_invalid_return_url_goes_back_to_login()
    {
        using var client = factory.CreateClient();

        using var callback = await client.SendAsync(HttpMethod.Get, "/account/external/callback?returnUrl=https%3A%2F%2Fevil.example");

        Assert.Equal(HttpStatusCode.Redirect, callback.StatusCode);
        Assert.Equal("/login?error=Validation.Failed", callback.Headers.Location!.OriginalString);
    }
}
