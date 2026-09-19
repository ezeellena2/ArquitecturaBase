using System.Net;
using ArquitecturaBase.Api.IntegrationTests.Support;
using ArquitecturaBase.Application.Features.Auth;
using Microsoft.AspNetCore.WebUtilities;

namespace ArquitecturaBase.Api.IntegrationTests.Auth;

[Collection(ApiTestGroup.Name)]
public sealed class ConnectFlowTests(ApiFactory factory)
{
    [Fact]
    public async Task Code_flow_issues_tokens_that_the_api_accepts()
    {
        using var client = factory.CreateClient();
        var email = TestEmails.Unique("flow");

        var tokens = await client.LoginAsync(factory, email);

        using var protectedResponse = await client.GetWithTokenAsync("/test/protected", tokens.AccessToken);
        using var userInfo = await client.GetWithTokenAsync("/connect/userinfo", tokens.AccessToken);
        var claims = await userInfo.ReadJsonAsync();

        Assert.NotNull(tokens.IdToken);
        Assert.Equal(HttpStatusCode.NoContent, protectedResponse.StatusCode);
        Assert.Equal(HttpStatusCode.OK, userInfo.StatusCode);
        Assert.Equal(email, claims.GetProperty("email").GetString());
        Assert.Equal("es", claims.GetProperty("locale").GetString());
        Assert.Equal(["User"], claims.GetProperty("role").EnumerateArray().Select(role => role.GetString()));
    }

    [Fact]
    public async Task Authorize_without_a_session_sends_the_user_to_login_with_the_original_request()
    {
        using var client = factory.CreateClient();

        using var response = await client.AuthorizeAsync(Pkce.ChallengeOf(Pkce.CreateVerifier()));

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        var location = response.Headers.Location!.OriginalString;
        Assert.StartsWith("/login?returnUrl=", location, StringComparison.Ordinal);

        var returnUrl = QueryHelpers.ParseQuery(location[location.IndexOf('?', StringComparison.Ordinal)..])["returnUrl"].ToString();
        Assert.True(ReturnUrls.IsAuthorizeRequest(returnUrl));
        Assert.Contains("code_challenge=", returnUrl, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Silent_renewal_without_a_session_returns_login_required()
    {
        using var client = factory.CreateClient();

        using var response = await client.AuthorizeAsync(Pkce.ChallengeOf(Pkce.CreateVerifier()), prompt: "none");

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Equal(ApiFactory.WebRedirectUri, response.Headers.Location!.GetLeftPart(UriPartial.Path));
        Assert.Equal("login_required", QueryHelpers.ParseQuery(response.Headers.Location.Query)["error"].ToString());
    }

    [Fact]
    public async Task Refresh_token_rotates_and_reusing_an_old_one_revokes_the_whole_chain()
    {
        using var client = factory.CreateClient();
        var tokens = await client.LoginAsync(factory, TestEmails.Unique("rotate"));

        using var firstRefresh = await client.RefreshAsync(tokens.RefreshToken);
        var rotated = await TokenResponse.ReadAsync(firstRefresh);
        using var reuse = await client.RefreshAsync(tokens.RefreshToken);
        using var afterReuse = await client.RefreshAsync(rotated.RefreshToken);
        using var api = await client.GetWithTokenAsync("/test/protected", rotated.AccessToken);

        Assert.NotEqual(tokens.RefreshToken, rotated.RefreshToken);
        Assert.Equal(HttpStatusCode.BadRequest, reuse.StatusCode);
        Assert.Equal("invalid_grant", (await reuse.ReadJsonAsync()).GetProperty("error").GetString());
        Assert.Equal(HttpStatusCode.BadRequest, afterReuse.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, api.StatusCode);
    }

    [Fact]
    public async Task Access_token_expires_after_15_minutes()
    {
        using var client = factory.CreateClient();
        var tokens = await client.LoginAsync(factory, TestEmails.Unique("expiry"));

        factory.Clock.Advance(TimeSpan.FromMinutes(16));
        using var response = await client.GetWithTokenAsync("/test/protected", tokens.AccessToken);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Revoked_refresh_token_can_no_longer_be_used()
    {
        using var client = factory.CreateClient();
        var tokens = await client.LoginAsync(factory, TestEmails.Unique("revoke"));

        using var revoke = await client.SendAsync(HttpMethod.Post, "/connect/revoke", new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["token"] = tokens.RefreshToken,
            ["token_type_hint"] = "refresh_token",
            ["client_id"] = "web",
        }));
        using var refresh = await client.RefreshAsync(tokens.RefreshToken);

        Assert.Equal(HttpStatusCode.OK, revoke.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, refresh.StatusCode);
    }

    [Fact]
    public async Task Logout_revokes_the_tokens_and_closes_the_session()
    {
        using var client = factory.CreateClient();
        var tokens = await client.LoginAsync(factory, TestEmails.Unique("logout"));

        using var logout = await client.SendAsync(HttpMethod.Get, QueryHelpers.AddQueryString("/connect/logout", new Dictionary<string, string?>
        {
            ["id_token_hint"] = tokens.IdToken,
            ["post_logout_redirect_uri"] = ApiFactory.PostLogoutRedirectUri,
        }));
        using var api = await client.GetWithTokenAsync("/test/protected", tokens.AccessToken);
        using var refresh = await client.RefreshAsync(tokens.RefreshToken);
        using var authorize = await client.AuthorizeAsync(Pkce.ChallengeOf(Pkce.CreateVerifier()));

        Assert.Equal(HttpStatusCode.Redirect, logout.StatusCode);
        Assert.Equal(ApiFactory.PostLogoutRedirectUri, logout.Headers.Location!.GetLeftPart(UriPartial.Path));
        Assert.Equal(HttpStatusCode.Unauthorized, api.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, refresh.StatusCode);
        Assert.StartsWith("/login?", authorize.Headers.Location!.OriginalString, StringComparison.Ordinal);
    }
}
