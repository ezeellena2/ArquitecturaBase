using System.Net;
using System.Net.Http.Headers;
using ArquitecturaBase.Api.IntegrationTests.Support;
using Microsoft.AspNetCore.WebUtilities;

namespace ArquitecturaBase.Api.IntegrationTests.Contracts;

/// <summary>Contrato observable de las variantes POST y de la ruta técnica de introspección.</summary>
[Collection(ApiTestGroup.Name)]
public sealed class AuthConnectHttpContractsTests(ApiFactory factory)
{
    [Fact]
    public async Task Post_authorize_without_a_session_preserves_the_form_in_the_login_return_url()
    {
        using var client = factory.CreateClient();
        using var response = await client.SendAsync(HttpMethod.Post, "/connect/authorize", AuthorizeForm());

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        var location = response.Headers.Location!.OriginalString;
        Assert.StartsWith("/login?returnUrl=", location, StringComparison.Ordinal);

        var returnUrl = QueryHelpers.ParseQuery(new Uri("https://localhost" + location).Query)["returnUrl"].ToString();
        Assert.StartsWith("/connect/authorize?", returnUrl, StringComparison.Ordinal);
        var original = QueryHelpers.ParseQuery(new Uri("https://localhost" + returnUrl).Query);
        Assert.Equal("code", original["response_type"].ToString());
        Assert.Equal("web", original["client_id"].ToString());
        Assert.Equal(ApiFactory.WebRedirectUri, original["redirect_uri"].ToString());
        Assert.Equal("contract-post-state", original["state"].ToString());
        Assert.Equal("S256", original["code_challenge_method"].ToString());
    }

    [Fact]
    public async Task Post_authorize_with_a_session_redirects_with_an_authorization_code()
    {
        using var client = factory.CreateClient();
        await client.SignInWithCodeAsync(factory, TestEmails.Unique("post-authorize"));

        using var response = await client.SendAsync(HttpMethod.Post, "/connect/authorize", AuthorizeForm());

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        var location = response.Headers.Location!;
        Assert.Equal(ApiFactory.WebRedirectUri, location.GetLeftPart(UriPartial.Path));
        var query = QueryHelpers.ParseQuery(location.Query);
        Assert.False(string.IsNullOrWhiteSpace(query["code"].ToString()));
        Assert.Equal("contract-post-state", query["state"].ToString());
    }

    [Fact]
    public async Task Post_userinfo_with_a_bearer_token_returns_the_scoped_claims_as_json()
    {
        using var client = factory.CreateClient();
        var email = TestEmails.Unique("post-userinfo");
        var tokens = await client.LoginAsync(factory, email);
        using var request = new HttpRequestMessage(HttpMethod.Post, "/connect/userinfo")
        {
            Content = new FormUrlEncodedContent([]),
        };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", tokens.AccessToken);

        using var response = await client.SendAsync(request, TestContext.Current.CancellationToken);
        var claims = await response.ReadJsonAsync();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("application/json", response.Content.Headers.ContentType?.MediaType);
        Assert.False(string.IsNullOrWhiteSpace(claims.GetProperty("sub").GetString()));
        Assert.Equal(email, claims.GetProperty("email").GetString());
        Assert.Equal("es", claims.GetProperty("locale").GetString());
        Assert.Equal(["User"], claims.GetProperty("role").EnumerateArray().Select(role => role.GetString()));
    }

    [Fact]
    public async Task Post_token_returns_uncacheable_json_without_a_session_cookie()
    {
        using var client = factory.CreateClient();
        var tokens = await client.LoginAsync(factory, TestEmails.Unique("token-headers"));

        using var response = await client.RefreshAsync(tokens.RefreshToken);
        var body = await response.ReadJsonAsync();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("application/json", response.Content.Headers.ContentType?.MediaType);
        Assert.False(string.IsNullOrWhiteSpace(body.GetProperty("access_token").GetString()));
        Assert.True(response.Headers.CacheControl?.NoStore);
        Assert.Contains(response.Headers.Pragma, value => value.Name == "no-cache");
        Assert.False(response.Headers.Contains("Set-Cookie"));
    }

    [Fact]
    public async Task Post_logout_redirects_clears_the_cookie_and_revokes_the_refresh_token()
    {
        using var client = factory.CreateClient();
        var tokens = await client.LoginAsync(factory, TestEmails.Unique("post-logout"));
        using var form = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["id_token_hint"] = tokens.IdToken!,
            ["post_logout_redirect_uri"] = ApiFactory.PostLogoutRedirectUri,
        });

        using var response = await client.SendAsync(HttpMethod.Post, "/connect/logout", form);
        using var refresh = await client.RefreshAsync(tokens.RefreshToken);
        using var authorize = await client.AuthorizeAsync(Pkce.ChallengeOf(Pkce.CreateVerifier()));

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Equal(ApiFactory.PostLogoutRedirectUri, response.Headers.Location!.GetLeftPart(UriPartial.Path));
        Assert.Contains(response.Headers.GetValues("Set-Cookie"), cookie =>
            cookie.StartsWith(".AspNetCore.Identity.Application=;", StringComparison.Ordinal));
        Assert.Equal(HttpStatusCode.BadRequest, refresh.StatusCode);
        Assert.Equal("invalid_grant", (await refresh.ReadJsonAsync()).GetProperty("error").GetString());
        Assert.Equal(HttpStatusCode.Redirect, authorize.StatusCode);
        Assert.StartsWith("/login?returnUrl=", authorize.Headers.Location!.OriginalString, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Introspection_is_registered_and_reports_an_unknown_token_as_inactive()
    {
        using var client = factory.CreateClient();
        using var response = await client.SendAsync(HttpMethod.Post, "/connect/introspect", new FormUrlEncodedContent(
            new Dictionary<string, string>
            {
                ["client_id"] = "web",
                ["token"] = "not-a-token",
            }));
        var body = await response.ReadJsonAsync();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("application/json", response.Content.Headers.ContentType?.MediaType);
        Assert.False(body.GetProperty("active").GetBoolean());
    }

    [Fact]
    public async Task Introspection_rejects_the_public_web_client_for_a_valid_access_token()
    {
        using var client = factory.CreateClient();
        var tokens = await client.LoginAsync(factory, TestEmails.Unique("introspect"));
        using var response = await client.SendAsync(HttpMethod.Post, "/connect/introspect", new FormUrlEncodedContent(
            new Dictionary<string, string>
            {
                ["client_id"] = "web",
                ["token"] = tokens.AccessToken,
                ["token_type_hint"] = "access_token",
            }));
        var body = await response.ReadJsonAsync();

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("application/json", response.Content.Headers.ContentType?.MediaType);
        Assert.Equal("unauthorized_client", body.GetProperty("error").GetString());
    }

    private static FormUrlEncodedContent AuthorizeForm() => new(new Dictionary<string, string>
    {
        ["response_type"] = "code",
        ["client_id"] = "web",
        ["redirect_uri"] = ApiFactory.WebRedirectUri,
        ["scope"] = AuthFlow.Scopes,
        ["code_challenge"] = Pkce.ChallengeOf(Pkce.CreateVerifier()),
        ["code_challenge_method"] = "S256",
        ["state"] = "contract-post-state",
    });
}
