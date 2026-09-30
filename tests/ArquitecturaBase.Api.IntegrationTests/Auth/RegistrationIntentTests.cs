using System.Net;
using ArquitecturaBase.Api.IntegrationTests.Support;
using ArquitecturaBase.Domain.Authentication;
using ArquitecturaBase.Domain.Settings;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.EntityFrameworkCore;

namespace ArquitecturaBase.Api.IntegrationTests.Auth;

[Collection(ApiTestGroup.Name)]
public sealed class RegistrationIntentTests(ApiFactory factory)
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Registration_creates_a_named_account_and_completes_oidc()
    {
        await using var mode = await RegistrationModeScope.SetAsync(factory, RegistrationMode.Open);
        using var client = factory.CreateClient();
        var email = TestEmails.Unique("registration");
        var code = await client.RequestCodeAsync(factory, email);
        using var verified = await client.PostJsonAsync("/account/login-code/verify",
            new { email, code, returnUrl = AuthFlow.AuthorizeReturnUrl, register = true, displayName = "Ana Perez" });
        Assert.Equal(HttpStatusCode.OK, verified.StatusCode);
        var verifier = Pkce.CreateVerifier();
        using var authorized = await client.AuthorizeAsync(Pkce.ChallengeOf(verifier));
        var tokens = await client.ExchangeCodeAsync(AuthFlow.CodeFromRedirect(authorized), verifier);
        using var me = await client.GetWithTokenAsync("/api/me", tokens.AccessToken);
        Assert.Equal("Ana Perez", (await me.ReadJsonAsync()).GetProperty("displayName").GetString());
    }

    [Fact]
    public async Task Login_does_not_register_an_unknown_account_or_issue_a_cookie()
    {
        await using var mode = await RegistrationModeScope.SetAsync(factory, RegistrationMode.Open);
        using var client = factory.CreateClient();
        var email = TestEmails.Unique("login-only");
        var code = await client.RequestCodeAsync(factory, email);
        using var response = await client.PostJsonAsync("/account/login-code/verify",
            new { email, code, returnUrl = AuthFlow.AuthorizeReturnUrl, register = false });
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Equal(AccountErrors.NotRegisteredCode, (await response.ReadJsonAsync()).GetProperty("code").GetString());
        Assert.False(await factory.ExecuteDbContextAsync(db => db.Users.AnyAsync(user => user.Email == email, Ct)));
        using var silent = await client.AuthorizeAsync(Pkce.ChallengeOf(Pkce.CreateVerifier()), prompt: "none");
        Assert.Equal("login_required", QueryHelpers.ParseQuery(silent.Headers.Location!.Query)["error"].ToString());
    }

    [Fact]
    public async Task Registration_does_not_overwrite_an_existing_account()
    {
        using var original = factory.CreateClient();
        var email = TestEmails.Unique("already-registered");
        await original.SignInWithCodeAsync(factory, email);
        factory.Clock.Advance(TimeSpan.FromSeconds(61));
        using var client = factory.CreateClient();
        var code = await client.RequestCodeAsync(factory, email);
        using var response = await client.PostJsonAsync("/account/login-code/verify",
            new { email, code, returnUrl = AuthFlow.AuthorizeReturnUrl, register = true, displayName = "Changed" });
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Equal(AccountErrors.AlreadyRegisteredCode, (await response.ReadJsonAsync()).GetProperty("code").GetString());
        Assert.Null(await factory.ExecuteDbContextAsync(db => db.Users.Where(user => user.Email == email).Select(user => user.DisplayName).SingleAsync(Ct)));
    }

    [Theory]
    [InlineData("")]
    [InlineData(" ")]
    [InlineData("Ana\0Perez")]
    public async Task Registration_validates_the_name_before_consuming_a_code(string displayName)
    {
        using var client = factory.CreateClient();
        using var response = await client.PostJsonAsync("/account/login-code/verify",
            new { email = TestEmails.Unique("invalid-name"), code = "123456", returnUrl = AuthFlow.AuthorizeReturnUrl, register = true, displayName });
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var problem = await response.ReadJsonAsync();
        Assert.Equal("Validation.Failed", problem.GetProperty("code").GetString());
        Assert.True(problem.GetProperty("errors").TryGetProperty("displayName", out _));
    }

    [Theory]
    [InlineData(RegistrationMode.Open, true)]
    [InlineData(RegistrationMode.InviteOnly, false)]
    public async Task Public_methods_report_the_current_registration_policy(RegistrationMode mode, bool expected)
    {
        await using var setting = await RegistrationModeScope.SetAsync(factory, mode);
        using var client = factory.CreateClient();
        using var response = await client.SendAsync(HttpMethod.Get, "/account/login-methods");
        Assert.Equal(expected, (await response.ReadJsonAsync()).GetProperty("registrationOpen").GetBoolean());
    }

    [Fact]
    public async Task Signup_hint_returns_to_registration_and_preserves_pkce()
    {
        using var client = factory.CreateClient();
        var challenge = Pkce.ChallengeOf(Pkce.CreateVerifier());
        var url = QueryHelpers.AddQueryString("/connect/authorize", new Dictionary<string, string?>
        {
            ["response_type"] = "code", ["client_id"] = "web",
            ["redirect_uri"] = ApiFactory.WebRedirectUri, ["scope"] = AuthFlow.Scopes,
            ["code_challenge"] = challenge, ["code_challenge_method"] = "S256",
            ["screen_hint"] = "signup", ["state"] = "signup-state",
        });
        using var response = await client.SendAsync(HttpMethod.Get, url);
        var location = response.Headers.Location!.OriginalString;
        Assert.StartsWith("/registro?returnUrl=", location, StringComparison.Ordinal);
        var returnUrl = QueryHelpers.ParseQuery(location[location.IndexOf('?', StringComparison.Ordinal)..])["returnUrl"].ToString();
        Assert.Contains(challenge, returnUrl, StringComparison.Ordinal);
        Assert.Contains("signup-state", returnUrl, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Google_registration_keeps_the_intent_when_an_account_already_exists()
    {
        using var original = factory.CreateClient();
        var email = TestEmails.Unique("google-existing");
        await original.SignInWithCodeAsync(factory, email);
        using var client = factory.CreateClient();
        using var external = await client.PostJsonAsync("/test/external-login",
            new { providerKey = "google-" + email, email, name = "Ana", emailVerified = true });
        using var response = await client.SendAsync(HttpMethod.Get,
            "/account/external/callback?register=true&returnUrl=" + Uri.EscapeDataString(AuthFlow.AuthorizeReturnUrl));
        var location = response.Headers.Location!.OriginalString;
        Assert.StartsWith("/registro?", location, StringComparison.Ordinal);
        Assert.Contains(AccountErrors.AlreadyRegisteredCode, location, StringComparison.Ordinal);
        Assert.Contains("returnUrl=", location, StringComparison.Ordinal);
    }
}
