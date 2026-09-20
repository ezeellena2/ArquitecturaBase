using System.Net;
using ArquitecturaBase.Api.IntegrationTests.Support;
using Microsoft.Extensions.DependencyInjection;
using OpenIddict.Abstractions;
using static OpenIddict.Abstractions.OpenIddictConstants;

namespace ArquitecturaBase.Api.IntegrationTests.Auth;

[Collection(ApiTestGroup.Name)]
public sealed class OpenIddictServerTests(ApiFactory factory)
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Discovery_document_publishes_the_endpoints_flows_and_scopes()
    {
        using var client = factory.CreateClient();

        using var response = await client.SendAsync(HttpMethod.Get, "/.well-known/openid-configuration");
        var document = await response.ReadJsonAsync();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("https://localhost/connect/authorize", document.GetProperty("authorization_endpoint").GetString());
        Assert.Equal("https://localhost/connect/token", document.GetProperty("token_endpoint").GetString());
        Assert.Equal("https://localhost/connect/logout", document.GetProperty("end_session_endpoint").GetString());
        Assert.Equal("https://localhost/connect/userinfo", document.GetProperty("userinfo_endpoint").GetString());
        Assert.Equal("https://localhost/connect/revoke", document.GetProperty("revocation_endpoint").GetString());
        Assert.Equal("https://localhost/connect/introspect", document.GetProperty("introspection_endpoint").GetString());

        var grantTypes = Strings(document, "grant_types_supported");
        Assert.Contains(GrantTypes.AuthorizationCode, grantTypes);
        Assert.Contains(GrantTypes.RefreshToken, grantTypes);
        Assert.DoesNotContain(GrantTypes.Password, grantTypes);
        Assert.DoesNotContain(GrantTypes.ClientCredentials, grantTypes);
        Assert.Contains(CodeChallengeMethods.Sha256, Strings(document, "code_challenge_methods_supported"));
        Assert.Contains("api", Strings(document, "scopes_supported"));
    }

    [Fact]
    public async Task Issuer_can_be_configured_for_the_public_origin()
    {
        await using var api = factory.WithWebHostBuilder(builder =>
            builder.UseSetting("Authentication:Issuer", "https://app.test/"));
        using var client = api.CreateClient();

        using var response = await client.SendAsync(HttpMethod.Get, "/.well-known/openid-configuration");
        var document = await response.ReadJsonAsync();

        Assert.Equal("https://app.test/", document.GetProperty("issuer").GetString());

        // OpenIddict arma los endpoints con el host del request, no con el issuer. Detrás del proxy de Vite salen
        // bien igual, porque la Api ve el Host del navegador (localhost:5173); acá el request entra por localhost.
        Assert.Equal("https://localhost/connect/authorize", document.GetProperty("authorization_endpoint").GetString());
    }

    [Fact]
    public async Task Web_client_is_public_requires_pkce_and_uses_the_configured_uris()
    {
        var (clientType, requirements, redirectUris, postLogoutRedirectUris) = await factory.ExecuteScopeAsync(async services =>
        {
            var manager = services.GetRequiredService<IOpenIddictApplicationManager>();
            var client = await manager.FindByClientIdAsync("web", Ct) ?? throw new InvalidOperationException("Missing client.");

            return (
                await manager.GetClientTypeAsync(client, Ct),
                await manager.GetRequirementsAsync(client, Ct),
                await manager.GetRedirectUrisAsync(client, Ct),
                await manager.GetPostLogoutRedirectUrisAsync(client, Ct));
        });

        Assert.Equal(ClientTypes.Public, clientType);
        Assert.Contains(Requirements.Features.ProofKeyForCodeExchange, requirements);
        Assert.Equal(ApiFactory.WebRedirectUri, Assert.Single(redirectUris));
        Assert.Equal(ApiFactory.PostLogoutRedirectUri, Assert.Single(postLogoutRedirectUris));
    }

    [Fact]
    public async Task Anonymous_api_request_is_challenged_by_the_bearer_scheme()
    {
        using var client = factory.CreateClient();

        using var response = await client.SendAsync(HttpMethod.Get, "/test/protected", language: "es");
        var problem = await response.ReadJsonAsync();

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Contains(response.Headers.WwwAuthenticate, header => header.Scheme == "Bearer");
        Assert.Equal("Http.Unauthorized", problem.GetProperty("code").GetString());
    }

    private static string[] Strings(System.Text.Json.JsonElement document, string property) =>
        document.GetProperty(property).EnumerateArray().Select(item => item.GetString()!).ToArray();
}
