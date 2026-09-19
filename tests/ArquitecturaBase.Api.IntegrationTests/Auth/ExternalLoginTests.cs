using System.Globalization;
using System.Net;
using ArquitecturaBase.Api.IntegrationTests.Support;
using ArquitecturaBase.Domain.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authentication.Google;
using Microsoft.AspNetCore.Identity;
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
            var success = await db.LoginAudits.SingleAsync(a => a.Email == email && a.Succeeded, Ct);
            return (user.DisplayName, login.LoginProvider, success);
        });
        Assert.Equal("Ana Pérez", displayName);
        Assert.Equal(GoogleDefaults.AuthenticationScheme, loginProvider);
        Assert.Equal(LoginMethod.Google, audit.Method);
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
}
