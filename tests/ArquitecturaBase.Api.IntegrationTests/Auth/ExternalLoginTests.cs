using System.Globalization;
using System.Net;
using ArquitecturaBase.Api.Contracts.Auth;
using ArquitecturaBase.Api.IntegrationTests.Support;
using ArquitecturaBase.Application.Interfaces.Persistence;
using ArquitecturaBase.Domain.Authentication;
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
using Microsoft.Extensions.DependencyInjection.Extensions;
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
        var probe = new FailedGoogleCommitProbe(email, providerKey);
        await using var api = factory.WithWebHostBuilder(builder => builder.ConfigureTestServices(services =>
        {
            services.RemoveAll<IUnitOfWork>();
            services.AddScoped<IUnitOfWork>(provider => new ThrowingGoogleCommitUnitOfWork(
                provider.GetRequiredService<ApplicationDbContext>(), probe));
        }));
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

        Assert.Equal(HttpStatusCode.InternalServerError, callback.StatusCode);
        var createdUserId = Assert.IsType<Guid>(probe.CreatedUserId);
        var persisted = await factory.ExecuteDbContextAsync(async db =>
            (User: await db.Users.IgnoreQueryFilters().AnyAsync(user => user.Id == createdUserId, Ct),
             Role: await db.UserRoles.AnyAsync(role => role.UserId == createdUserId, Ct),
             Link: await db.UserLogins.AnyAsync(login => login.LoginProvider == "Google" && login.ProviderKey == providerKey, Ct),
             Audit: await db.LoginAudits.AnyAsync(audit => audit.Identifier == email, Ct)));
        Assert.Equal((false, false, false, false), persisted);
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

    private sealed class FailedGoogleCommitProbe(string email, string providerKey)
    {
        public string Email { get; } = email;

        public string ProviderKey { get; } = providerKey;

        public Guid? CreatedUserId { get; set; }
    }

    private sealed class ThrowingGoogleCommitUnitOfWork(ApplicationDbContext db, FailedGoogleCommitProbe probe) : IUnitOfWork
    {
        public async Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
        {
            Assert.NotNull(db.Database.CurrentTransaction);
            await db.SaveChangesAsync(cancellationToken);

            var user = await db.Users.AsNoTracking().SingleAsync(user => user.Email == probe.Email, cancellationToken);
            probe.CreatedUserId = user.Id;
            Assert.True(await db.UserRoles.AnyAsync(role => role.UserId == user.Id, cancellationToken));
            Assert.True(await db.UserLogins.AnyAsync(login => login.LoginProvider == "Google"
                && login.ProviderKey == probe.ProviderKey, cancellationToken));
            Assert.True(await db.LoginAudits.AnyAsync(audit => audit.Identifier == probe.Email
                && audit.Succeeded, cancellationToken));

            throw new ExpectedGoogleCommitFailure();
        }
    }

    private sealed class ExpectedGoogleCommitFailure : Exception;
}
