using ArquitecturaBase.Infrastructure.Identity.OpenIddict;
using Microsoft.Extensions.Options;
using OpenIddict.Abstractions;
using static OpenIddict.Abstractions.OpenIddictConstants;

namespace ArquitecturaBase.Infrastructure.Persistence.Seed;

/// <summary>El scope "api" y el cliente público "web" con PKCE. Si ya existen, los actualiza con la configuración actual.</summary>
internal sealed class OpenIddictSeeder(
    IOpenIddictApplicationManager applicationManager,
    IOpenIddictScopeManager scopeManager,
    IOptions<WebClientOptions> webClientOptions)
{
    public async Task SeedAsync(CancellationToken cancellationToken)
    {
        await SeedApiScopeAsync(cancellationToken);
        await SeedWebClientAsync(cancellationToken);
    }

    private async Task SeedApiScopeAsync(CancellationToken cancellationToken)
    {
        var descriptor = new OpenIddictScopeDescriptor
        {
            Name = AuthServerDefaults.ApiScope,
            DisplayName = "ArquitecturaBase API",
        };
        descriptor.Resources.Add(AuthServerDefaults.ApiResource);

        var scope = await scopeManager.FindByNameAsync(AuthServerDefaults.ApiScope, cancellationToken);

        if (scope is null)
        {
            await scopeManager.CreateAsync(descriptor, cancellationToken);
        }
        else
        {
            await scopeManager.UpdateAsync(scope, descriptor, cancellationToken);
        }
    }

    private async Task SeedWebClientAsync(CancellationToken cancellationToken)
    {
        var descriptor = new OpenIddictApplicationDescriptor
        {
            ClientId = AuthServerDefaults.WebClientId,
            ClientType = ClientTypes.Public,
            ConsentType = ConsentTypes.Implicit,
            DisplayName = "ArquitecturaBase Web",
        };

        descriptor.Permissions.UnionWith(
        [
            Permissions.Endpoints.Authorization,
            Permissions.Endpoints.Token,
            Permissions.Endpoints.EndSession,
            Permissions.Endpoints.Revocation,
            Permissions.GrantTypes.AuthorizationCode,
            Permissions.GrantTypes.RefreshToken,
            Permissions.ResponseTypes.Code,
            Permissions.Scopes.Email,
            Permissions.Scopes.Profile,
            Permissions.Scopes.Roles,
            Permissions.Prefixes.Scope + AuthServerDefaults.ApiScope,
        ]);
        descriptor.Requirements.Add(Requirements.Features.ProofKeyForCodeExchange);
        descriptor.RedirectUris.UnionWith(webClientOptions.Value.RedirectUris);
        descriptor.PostLogoutRedirectUris.UnionWith(webClientOptions.Value.PostLogoutRedirectUris);

        var client = await applicationManager.FindByClientIdAsync(AuthServerDefaults.WebClientId, cancellationToken);

        if (client is null)
        {
            await applicationManager.CreateAsync(descriptor, cancellationToken);
        }
        else
        {
            await applicationManager.UpdateAsync(client, descriptor, cancellationToken);
        }
    }
}
