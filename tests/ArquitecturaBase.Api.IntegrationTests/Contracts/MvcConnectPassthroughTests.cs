using System.Globalization;
using System.Net;
using System.Net.Http.Headers;
using System.Security.Claims;
using ArquitecturaBase.Api.Endpoints;
using ArquitecturaBase.Api.Endpoints.Connect;
using ArquitecturaBase.Api.IntegrationTests.Support;
using ArquitecturaBase.Application.Models.Auth;
using ArquitecturaBase.Application.Interfaces.Integrations;
using Microsoft.AspNetCore;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Controllers;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.Routing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using OpenIddict.Abstractions;
using OpenIddict.Server.AspNetCore;
using static OpenIddict.Abstractions.OpenIddictConstants;

namespace ArquitecturaBase.Api.IntegrationTests.Contracts;

/// <summary>
/// Prueba el passthrough real de OpenIddict por MVC. El host clonado mapea un controller solo de test en las mismas
/// URIs configuradas para OpenIddict y retira únicamente los cuatro IEndpoint viejos, sin tocar la Api de producción.
/// </summary>
[Collection(ApiTestGroup.Name)]
public sealed class MvcConnectPassthroughTests(ApiFactory factory)
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public void Test_host_maps_each_connect_method_once_to_the_mvc_controller()
    {
        using var api = MvcApi();
        _ = api.Services;

        var endpoints = api.Services.GetRequiredService<EndpointDataSource>().Endpoints
            .OfType<RouteEndpoint>()
            .Where(endpoint => endpoint.RoutePattern.RawText?.StartsWith("connect/", StringComparison.OrdinalIgnoreCase) == true)
            .ToArray();
        var methods = endpoints.SelectMany(endpoint =>
            (endpoint.Metadata.GetMetadata<IHttpMethodMetadata>()?.HttpMethods ?? [])
                .Select(method => $"{method} /{endpoint.RoutePattern.RawText}"))
            .Order(StringComparer.Ordinal)
            .ToArray();

        Assert.Equal([
            "GET /connect/authorize",
            "GET /connect/logout",
            "GET /connect/userinfo",
            "POST /connect/authorize",
            "POST /connect/logout",
            "POST /connect/token",
            "POST /connect/userinfo",
        ], methods);
        Assert.All(endpoints, endpoint => Assert.Equal(typeof(MvcConnectProbeController),
            endpoint.Metadata.GetMetadata<ControllerActionDescriptor>()?.ControllerTypeInfo.AsType()));
    }

    [Theory]
    [InlineData("GET")]
    [InlineData("POST")]
    public async Task Authorize_without_cookie_uses_mvc_redirect_or_forbid(string method)
    {
        await using var api = MvcApi();
        using var client = api.CreateClient();
        using var redirect = await AuthorizeAsync(client, method);
        using var forbidden = await AuthorizeAsync(client, method, prompt: "none");

        Assert.Equal(HttpStatusCode.Redirect, redirect.StatusCode);
        var loginLocation = redirect.Headers.Location!.OriginalString;
        Assert.StartsWith("/login?returnUrl=", loginLocation, StringComparison.Ordinal);
        var returnUrl = QueryHelpers.ParseQuery(new Uri("https://localhost" + loginLocation).Query)["returnUrl"].ToString();
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
        await using var api = MvcApi();
        using var client = api.CreateClient();
        var email = TestEmails.Unique("mvc-connect-flow");
        await client.SignInWithCodeAsync(factory, email);
        var verifier = Pkce.CreateVerifier();

        using var authorization = await AuthorizeAsync(client, "POST", Pkce.ChallengeOf(verifier));
        Assert.Equal(HttpStatusCode.Redirect, authorization.StatusCode);
        var code = QueryHelpers.ParseQuery(authorization.Headers.Location!.Query)["code"].ToString();
        Assert.False(string.IsNullOrWhiteSpace(code));
        Assert.Equal("mvc-state", QueryHelpers.ParseQuery(authorization.Headers.Location.Query)["state"].ToString());

        var tokens = await client.ExchangeCodeAsync(code, verifier);
        using var userInfoGet = await client.GetWithTokenAsync("/connect/userinfo", tokens.AccessToken);
        using var userInfoPost = await UserInfoPostAsync(client, tokens.AccessToken);
        using var refresh = await client.RefreshAsync(tokens.RefreshToken);

        Assert.Equal(HttpStatusCode.OK, userInfoGet.StatusCode);
        Assert.Equal(HttpStatusCode.OK, userInfoPost.StatusCode);
        Assert.Equal(email, (await userInfoGet.ReadJsonAsync()).GetProperty("email").GetString());
        Assert.Equal(email, (await userInfoPost.ReadJsonAsync()).GetProperty("email").GetString());
        Assert.Equal(HttpStatusCode.OK, refresh.StatusCode);
        Assert.Equal("application/json", refresh.Content.Headers.ContentType?.MediaType);
        Assert.True(refresh.Headers.CacheControl?.NoStore);
        Assert.Contains(refresh.Headers.Pragma, value => value.Name == "no-cache");
        Assert.False(refresh.Headers.Contains("Set-Cookie"));
    }

    [Fact]
    public async Task Mvc_token_forbid_and_userinfo_challenge_keep_oauth_errors()
    {
        await using var api = MvcApi();
        using var client = api.CreateClient();
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
        await using var api = MvcApi();
        using var client = api.CreateClient();
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

    private WebApplicationFactory<Program> MvcApi() => factory.WithWebHostBuilder(builder => builder.ConfigureTestServices(services =>
    {
        foreach (var descriptor in services.Where(descriptor => descriptor.ServiceType == typeof(IEndpoint)
                     && descriptor.ImplementationType?.Namespace == typeof(AuthorizeEndpoint).Namespace).ToArray())
        {
            services.Remove(descriptor);
        }

        services.AddControllers().AddApplicationPart(typeof(MvcConnectProbeController).Assembly);
    }));

    private static async Task<HttpResponseMessage> AuthorizeAsync(
        HttpClient client, string method, string? challenge = null, string? prompt = null)
    {
        var parameters = new Dictionary<string, string>
        {
            ["response_type"] = "code",
            ["client_id"] = "web",
            ["redirect_uri"] = ApiFactory.WebRedirectUri,
            ["scope"] = AuthFlow.Scopes,
            ["code_challenge"] = challenge ?? Pkce.ChallengeOf(Pkce.CreateVerifier()),
            ["code_challenge_method"] = "S256",
            ["state"] = "mvc-state",
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

/// <summary>Implementación MVC de prueba de los cuatro endpoints passthrough de OpenIddict.</summary>
[ApiController]
[AllowAnonymous]
[ApiExplorerSettings(IgnoreApi = true)]
[Route("connect")]
public sealed class MvcConnectProbeController(
    IServiceProvider services,
    IIdentityService identityService,
    IOpenIddictTokenManager tokenManager) : ControllerBase
{
    [HttpGet("authorize")]
    [HttpPost("authorize")]
    public async Task<IActionResult> Authorize(CancellationToken cancellationToken)
    {
        var request = HttpContext.GetOpenIddictServerRequest()
            ?? throw new InvalidOperationException("The OpenID Connect request cannot be retrieved.");
        var session = await HttpContext.AuthenticateAsync(IdentityConstants.ApplicationScheme);
        var userId = Guid.TryParse(session.Principal?.FindFirstValue(ClaimTypes.NameIdentifier), CultureInfo.InvariantCulture, out var id)
            ? id : (Guid?)null;
        var principal = session.Succeeded && userId is not null
            ? await services.GetRequiredService<OpenIdPrincipalFactory>()
                .CreateAsync(userId.Value, request.GetScopes(), cancellationToken) : null;
        if (principal is not null)
        {
            return SignIn(principal, OpenIddictServerAspNetCoreDefaults.AuthenticationScheme);
        }

        if (session.Succeeded) await HttpContext.SignOutAsync(IdentityConstants.ApplicationScheme);
        if (request.HasPromptValue(PromptValues.None))
        {
            return Forbid(ErrorProperties(Errors.LoginRequired, "The user is not signed in."),
                OpenIddictServerAspNetCoreDefaults.AuthenticationScheme);
        }

        var parameters = Request.HasFormContentType
            ? (await Request.ReadFormAsync(cancellationToken)).ToList()
            : Request.Query.ToList();
        return Redirect(ReturnUrls.LoginPath + QueryString.Create("returnUrl",
            ReturnUrls.AuthorizePath + QueryString.Create(parameters)));
    }

    [HttpPost("token")]
    public async Task<IActionResult> Token(CancellationToken cancellationToken)
    {
        var request = HttpContext.GetOpenIddictServerRequest()
            ?? throw new InvalidOperationException("The OpenID Connect request cannot be retrieved.");
        if (!request.IsAuthorizationCodeGrantType() && !request.IsRefreshTokenGrantType())
        {
            throw new InvalidOperationException("The grant type is not supported.");
        }

        var stored = await HttpContext.AuthenticateAsync(OpenIddictServerAspNetCoreDefaults.AuthenticationScheme);
        var principal = stored.Principal is null
            ? null : await services.GetRequiredService<OpenIdPrincipalFactory>()
                .RefreshAsync(stored.Principal, cancellationToken);
        return principal is null
            ? Forbid(ErrorProperties(Errors.InvalidGrant, "The user can no longer sign in."),
                OpenIddictServerAspNetCoreDefaults.AuthenticationScheme)
            : SignIn(principal, OpenIddictServerAspNetCoreDefaults.AuthenticationScheme);
    }

    [HttpGet("logout")]
    [HttpPost("logout")]
    public async Task<IActionResult> Logout(CancellationToken cancellationToken)
    {
        var hint = await HttpContext.AuthenticateAsync(OpenIddictServerAspNetCoreDefaults.AuthenticationScheme);
        var authorizationId = hint.Principal?.GetAuthorizationId();
        if (!string.IsNullOrEmpty(authorizationId))
        {
            await tokenManager.RevokeByAuthorizationIdAsync(authorizationId, cancellationToken);
        }

        await HttpContext.SignOutAsync(IdentityConstants.ApplicationScheme);
        return SignOut(new AuthenticationProperties { RedirectUri = ReturnUrls.LoginPath },
            OpenIddictServerAspNetCoreDefaults.AuthenticationScheme);
    }

    [HttpGet("userinfo")]
    [HttpPost("userinfo")]
    public async Task<IActionResult> UserInfo(CancellationToken cancellationToken)
    {
        var principal = (await HttpContext.AuthenticateAsync(OpenIddictServerAspNetCoreDefaults.AuthenticationScheme)).Principal;
        var user = Guid.TryParse(principal?.GetClaim(Claims.Subject), CultureInfo.InvariantCulture, out var userId)
            ? await identityService.FindByIdAsync(userId, cancellationToken) : null;
        if (principal is null || user is not { IsActive: true })
        {
            return Challenge(ErrorProperties(Errors.InvalidToken, "The user no longer exists or is disabled."),
                OpenIddictServerAspNetCoreDefaults.AuthenticationScheme);
        }

        var claims = new Dictionary<string, object>(StringComparer.Ordinal)
        {
            [Claims.Subject] = user.Id.ToString("D", CultureInfo.InvariantCulture),
        };
        if (principal.HasScope(Scopes.Email) && user.Email is not null)
        {
            claims[Claims.Email] = user.Email;
            claims[Claims.EmailVerified] = user.EmailConfirmed;
        }
        if (principal.HasScope(Scopes.Profile))
        {
            if (OpenIdPrincipalFactory.NameOf(user) is { } name) claims[Claims.Name] = name;
            claims[Claims.Locale] = user.Culture;
            claims[Claims.Zoneinfo] = user.TimeZoneId;
        }
        if (principal.HasScope(Scopes.Roles))
        {
            claims[Claims.Role] = await identityService.GetRolesAsync(user.Id, cancellationToken);
        }
        return Ok(claims);
    }

    private static AuthenticationProperties ErrorProperties(string error, string description) =>
        new(new Dictionary<string, string?>
        {
            [OpenIddictServerAspNetCoreConstants.Properties.Error] = error,
            [OpenIddictServerAspNetCoreConstants.Properties.ErrorDescription] = description,
        });
}
