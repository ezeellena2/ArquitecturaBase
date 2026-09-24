using System.Net;
using System.Net.Http.Headers;
using ArquitecturaBase.Api.Controllers;
using ArquitecturaBase.Api.IntegrationTests.Support;
using Microsoft.AspNetCore.Mvc.Controllers;
using Microsoft.AspNetCore.Routing;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace ArquitecturaBase.Api.IntegrationTests.Contracts;

/// <summary>Comprueba el passthrough MVC de las rutas reales, sin host ni controller de prueba.</summary>
[Collection(ApiTestGroup.Name)]
public sealed class MvcConnectPassthroughTests(ApiFactory factory)
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public void Production_host_maps_each_connect_method_once_to_the_mvc_controller()
    {
        _ = factory.Services;
        var endpoints = factory.Services.GetRequiredService<EndpointDataSource>().Endpoints
            .OfType<RouteEndpoint>()
            .Where(endpoint => endpoint.RoutePattern.RawText?.StartsWith("connect/", StringComparison.OrdinalIgnoreCase) == true)
            .ToArray();
        var methods = endpoints.SelectMany(endpoint =>
            (endpoint.Metadata.GetMetadata<IHttpMethodMetadata>()?.HttpMethods ?? [])
                .Select(method => $"{method} /{endpoint.RoutePattern.RawText}"))
            .Order(StringComparer.Ordinal)
            .ToArray();
        Assert.Equal([
            "GET /connect/authorize", "GET /connect/logout", "GET /connect/userinfo",
            "POST /connect/authorize", "POST /connect/logout", "POST /connect/token", "POST /connect/userinfo",
        ], methods);
        Assert.All(endpoints, endpoint => Assert.Equal(typeof(ConnectController),
            endpoint.Metadata.GetMetadata<ControllerActionDescriptor>()?.ControllerTypeInfo.AsType()));
    }

    [Theory]
    [InlineData("GET")]
    [InlineData("POST")]
    public async Task Authorize_without_cookie_uses_mvc_redirect_or_forbid(string method)
    {
        using var client = factory.CreateClient();
        using var redirect = await AuthorizeAsync(client, method);
        using var forbidden = await AuthorizeAsync(client, method, prompt: "none");
        Assert.Equal(HttpStatusCode.Redirect, redirect.StatusCode);
        var location = redirect.Headers.Location!.OriginalString;
        Assert.StartsWith("/login?returnUrl=", location, StringComparison.Ordinal);
        var returnUrl = QueryHelpers.ParseQuery(new Uri("https://localhost" + location).Query)["returnUrl"].ToString();
        Assert.StartsWith("/connect/authorize?", returnUrl, StringComparison.Ordinal);
        Assert.Equal("mvc-state", QueryHelpers.ParseQuery(new Uri("https://localhost" + returnUrl).Query)["state"].ToString());
        Assert.Equal(HttpStatusCode.Redirect, forbidden.StatusCode);
        Assert.Equal(ApiFactory.WebRedirectUri, forbidden.Headers.Location!.GetLeftPart(UriPartial.Path));
        var error = QueryHelpers.ParseQuery(forbidden.Headers.Location.Query);
        Assert.Equal("login_required", error["error"].ToString());
        Assert.Equal("mvc-state", error["state"].ToString());
    }

    [Fact]
    public async Task Mvc_authorize_and_form_token_exchange_issue_tokens_usable_by_userinfo()
    {
        using var client = factory.CreateClient();
        var email = TestEmails.Unique("mvc-connect-flow");
        await client.SignInWithCodeAsync(factory, email);
        var verifier = Pkce.CreateVerifier();
        using var authorization = await AuthorizeAsync(client, "POST", Pkce.ChallengeOf(verifier));
        Assert.Equal(HttpStatusCode.Redirect, authorization.StatusCode);
        var code = QueryHelpers.ParseQuery(authorization.Headers.Location!.Query)["code"].ToString();
        Assert.False(string.IsNullOrWhiteSpace(code));
        Assert.Equal("mvc-state", QueryHelpers.ParseQuery(authorization.Headers.Location.Query)["state"].ToString());
        var tokens = await client.ExchangeCodeAsync(code, verifier);
        using var get = await client.GetWithTokenAsync("/connect/userinfo", tokens.AccessToken);
        using var post = await UserInfoPostAsync(client, tokens.AccessToken);
        using var refresh = await client.RefreshAsync(tokens.RefreshToken);
        Assert.Equal(HttpStatusCode.OK, get.StatusCode);
        Assert.Equal(HttpStatusCode.OK, post.StatusCode);
        Assert.Equal(email, (await get.ReadJsonAsync()).GetProperty("email").GetString());
        Assert.Equal(email, (await post.ReadJsonAsync()).GetProperty("email").GetString());
        Assert.Equal(HttpStatusCode.OK, refresh.StatusCode);
        Assert.Equal("application/json", refresh.Content.Headers.ContentType?.MediaType);
        Assert.True(refresh.Headers.CacheControl?.NoStore);
        Assert.Contains(refresh.Headers.Pragma, value => value.Name == "no-cache");
        Assert.False(refresh.Headers.Contains("Set-Cookie"));
    }

    [Fact]
    public async Task Mvc_token_forbid_and_userinfo_challenge_keep_oauth_errors()
    {
        using var client = factory.CreateClient();
        var email = TestEmails.Unique("mvc-connect-disabled");
        await client.SignInWithCodeAsync(factory, email);
        var verifier = Pkce.CreateVerifier();
        using var authorization = await AuthorizeAsync(client, "GET", Pkce.ChallengeOf(verifier));
        var code = QueryHelpers.ParseQuery(authorization.Headers.Location!.Query)["code"].ToString();
        var tokens = await client.ExchangeCodeAsync(code, verifier);
        await factory.ExecuteDbContextAsync(async db =>
        {
            var user = await db.Users.SingleAsync(candidate => candidate.Email == email, Ct);
            user.IsActive = false;
            return await db.SaveChangesAsync(Ct);
        });
        using var forbidden = await client.RefreshAsync(tokens.RefreshToken);
        using var challenged = await client.GetWithTokenAsync("/connect/userinfo", tokens.AccessToken);
        Assert.Equal(HttpStatusCode.BadRequest, forbidden.StatusCode);
        Assert.Equal("invalid_grant", (await forbidden.ReadJsonAsync()).GetProperty("error").GetString());
        Assert.Equal(HttpStatusCode.Unauthorized, challenged.StatusCode);
        Assert.Contains(challenged.Headers.WwwAuthenticate, header =>
            string.Equals(header.Scheme, "Bearer", StringComparison.OrdinalIgnoreCase));
    }

    [Theory]
    [InlineData("GET")]
    [InlineData("POST")]
    public async Task Mvc_logout_redirects_clears_cookie_and_revokes_refresh_token(string method)
    {
        using var client = factory.CreateClient();
        var tokens = await client.LoginAsync(factory, TestEmails.Unique("mvc-connect-logout"));
        using var logout = method == "POST"
            ? await client.SendAsync(HttpMethod.Post, "/connect/logout", new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["id_token_hint"] = tokens.IdToken!,
                ["post_logout_redirect_uri"] = ApiFactory.PostLogoutRedirectUri,
            }))
            : await client.SendAsync(HttpMethod.Get, QueryHelpers.AddQueryString("/connect/logout", new Dictionary<string, string?>
            {
                ["id_token_hint"] = tokens.IdToken,
                ["post_logout_redirect_uri"] = ApiFactory.PostLogoutRedirectUri,
            }));
        using var refresh = await client.RefreshAsync(tokens.RefreshToken);
        Assert.Equal(HttpStatusCode.Redirect, logout.StatusCode);
        Assert.Equal(ApiFactory.PostLogoutRedirectUri, logout.Headers.Location!.GetLeftPart(UriPartial.Path));
        Assert.Contains(logout.Headers.GetValues("Set-Cookie"), cookie =>
            cookie.StartsWith(".AspNetCore.Identity.Application=;", StringComparison.Ordinal));
        Assert.Equal(HttpStatusCode.BadRequest, refresh.StatusCode);
        Assert.Equal("invalid_grant", (await refresh.ReadJsonAsync()).GetProperty("error").GetString());
    }

    private static async Task<HttpResponseMessage> AuthorizeAsync(
        HttpClient client, string method, string? challenge = null, string? prompt = null)
    {
        var parameters = new Dictionary<string, string>
        {
            ["response_type"] = "code", ["client_id"] = "web", ["redirect_uri"] = ApiFactory.WebRedirectUri,
            ["scope"] = AuthFlow.Scopes, ["code_challenge"] = challenge ?? Pkce.ChallengeOf(Pkce.CreateVerifier()),
            ["code_challenge_method"] = "S256", ["state"] = "mvc-state",
        };
        if (prompt is not null) parameters["prompt"] = prompt;
        return method == "POST"
            ? await client.SendAsync(HttpMethod.Post, "/connect/authorize", new FormUrlEncodedContent(parameters))
            : await client.SendAsync(HttpMethod.Get, QueryHelpers.AddQueryString("/connect/authorize",
                parameters.ToDictionary(pair => pair.Key, pair => (string?)pair.Value)));
    }

    private static async Task<HttpResponseMessage> UserInfoPostAsync(HttpClient client, string accessToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, "/connect/userinfo")
        {
            Content = new FormUrlEncodedContent([]),
        };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
        return await client.SendAsync(request, Ct);
    }
}
