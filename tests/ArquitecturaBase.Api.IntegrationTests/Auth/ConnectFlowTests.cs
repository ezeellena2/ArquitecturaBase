using System.Net;
using ArquitecturaBase.Api.IntegrationTests.Support;
using ArquitecturaBase.Application.Features.Auth;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.EntityFrameworkCore;

namespace ArquitecturaBase.Api.IntegrationTests.Auth;

[Collection(ApiTestGroup.Name)]
public sealed class ConnectFlowTests(ApiFactory factory)
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

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
        var original = await client.LoginAsync(factory, TestEmails.Unique("rotate"));

        using var firstRefresh = await client.RefreshAsync(original.RefreshToken);
        var firstRotation = await TokenResponse.ReadAsync(firstRefresh);
        using var firstRotationApiCall = await client.GetWithTokenAsync("/test/protected", firstRotation.AccessToken);

        using var secondRefresh = await client.RefreshAsync(firstRotation.RefreshToken);
        var secondRotation = await TokenResponse.ReadAsync(secondRefresh);
        using var secondRotationApiCall = await client.GetWithTokenAsync("/test/protected", secondRotation.AccessToken);

        using var originalReuse = await client.RefreshAsync(original.RefreshToken);
        using var refreshAfterReuse = await client.RefreshAsync(secondRotation.RefreshToken);
        using var apiCallAfterReuse = await client.GetWithTokenAsync("/test/protected", secondRotation.AccessToken);

        Assert.NotEqual(original.RefreshToken, firstRotation.RefreshToken);
        Assert.NotEqual(firstRotation.RefreshToken, secondRotation.RefreshToken);
        Assert.Equal(HttpStatusCode.NoContent, firstRotationApiCall.StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, secondRotationApiCall.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, originalReuse.StatusCode);
        Assert.Equal("invalid_grant", (await originalReuse.ReadJsonAsync()).GetProperty("error").GetString());
        Assert.Equal(HttpStatusCode.BadRequest, refreshAfterReuse.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, apiCallAfterReuse.StatusCode);
    }

    [Fact]
    public async Task Access_token_expires_after_15_minutes()
    {
        using var client = factory.CreateClient();
        var tokens = await client.LoginAsync(factory, TestEmails.Unique("expiry"));

        factory.Clock.Advance(TimeSpan.FromMinutes(14));
        using var beforeExpiry = await client.GetWithTokenAsync("/test/protected", tokens.AccessToken);

        factory.Clock.Advance(TimeSpan.FromMinutes(2));
        using var afterExpiry = await client.GetWithTokenAsync("/test/protected", tokens.AccessToken);

        Assert.Equal(HttpStatusCode.NoContent, beforeExpiry.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, afterExpiry.StatusCode);
    }

    [Fact]
    public async Task Userinfo_rejects_the_access_token_of_a_disabled_account()
    {
        using var client = factory.CreateClient();
        var email = TestEmails.Unique("disabled");
        var tokens = await client.LoginAsync(factory, email);

        await factory.ExecuteDbContextAsync(async db =>
        {
            var user = await db.Users.SingleAsync(candidate => candidate.Email == email, Ct);
            user.IsActive = false;

            return await db.SaveChangesAsync(Ct);
        });
        using var userInfo = await client.GetWithTokenAsync("/connect/userinfo", tokens.AccessToken);

        Assert.Equal(HttpStatusCode.Unauthorized, userInfo.StatusCode);
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
