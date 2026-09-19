using System.Collections.Immutable;
using System.Globalization;
using System.Security.Claims;
using ArquitecturaBase.Application.Abstractions.Identity;
using Microsoft.IdentityModel.Tokens;
using OpenIddict.Abstractions;
using static OpenIddict.Abstractions.OpenIddictConstants;

namespace ArquitecturaBase.Api.Endpoints.Connect;

/// <summary>
/// Arma la identidad que OpenIddict convierte en tokens: sub, email, name y role (sección 5.6). Los permisos no van
/// en el token. Vive en la Api, y no en Infrastructure como dice el spec, porque la Api no puede usar tipos de
/// Infrastructure fuera de Program.cs.
/// </summary>
internal sealed class OpenIdPrincipalFactory(IIdentityService identityService, IOpenIddictScopeManager scopeManager)
{
    /// <summary>Para /connect/authorize. Null si la cuenta ya no puede ingresar.</summary>
    public async Task<ClaimsPrincipal?> CreateAsync(Guid userId, ImmutableArray<string> scopes, CancellationToken cancellationToken)
    {
        var identity = new ClaimsIdentity(TokenValidationParameters.DefaultAuthenticationType, Claims.Name, Claims.Role);

        if (!await SetUserClaimsAsync(identity, userId, cancellationToken))
        {
            return null;
        }

        var resources = new List<string>();

        await foreach (var resource in scopeManager.ListResourcesAsync(scopes, cancellationToken))
        {
            resources.Add(resource);
        }

        identity.SetScopes(scopes);
        identity.SetResources(resources);
        identity.SetDestinations(GetDestinations);

        return new ClaimsPrincipal(identity);
    }

    /// <summary>
    /// Para /connect/token. Parte de lo guardado en el code o el refresh token: los scopes y la autorización, que
    /// OpenIddict necesita para revocar toda la cadena. Actualiza los datos del usuario. Null si ya no puede ingresar.
    /// </summary>
    public async Task<ClaimsPrincipal?> RefreshAsync(ClaimsPrincipal stored, CancellationToken cancellationToken)
    {
        if (!Guid.TryParse(stored.GetClaim(Claims.Subject), CultureInfo.InvariantCulture, out var userId))
        {
            return null;
        }

        var identity = new ClaimsIdentity(stored.Claims, TokenValidationParameters.DefaultAuthenticationType, Claims.Name, Claims.Role);

        if (!await SetUserClaimsAsync(identity, userId, cancellationToken))
        {
            return null;
        }

        identity.SetDestinations(GetDestinations);

        return new ClaimsPrincipal(identity);
    }

    private async Task<bool> SetUserClaimsAsync(ClaimsIdentity identity, Guid userId, CancellationToken cancellationToken)
    {
        var user = await identityService.FindByIdAsync(userId, cancellationToken);

        if (user is not { IsActive: true })
        {
            return false;
        }

        var roles = await identityService.GetRolesAsync(user.Id, cancellationToken);

        identity
            .SetClaim(Claims.Subject, user.Id.ToString("D", CultureInfo.InvariantCulture))
            .SetClaim(Claims.Email, user.Email)
            .SetClaim(Claims.Name, user.DisplayName ?? user.Email)
            .SetClaims(Claims.Role, [.. roles]);

        return true;
    }

    // El access token lleva siempre sub, email, name y role (los usa la Api); el id token, según los scopes pedidos.
    private static IEnumerable<string> GetDestinations(Claim claim) => claim.Type switch
    {
        Claims.Subject => [Destinations.AccessToken, Destinations.IdentityToken],
        Claims.Email when claim.Subject?.HasScope(Scopes.Email) == true => [Destinations.AccessToken, Destinations.IdentityToken],
        Claims.Name when claim.Subject?.HasScope(Scopes.Profile) == true => [Destinations.AccessToken, Destinations.IdentityToken],
        Claims.Role when claim.Subject?.HasScope(Scopes.Roles) == true => [Destinations.AccessToken, Destinations.IdentityToken],
        Claims.Email or Claims.Name or Claims.Role => [Destinations.AccessToken],
        _ => [],
    };
}
