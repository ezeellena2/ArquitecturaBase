using System.Collections.Immutable;
using System.Globalization;
using System.Security.Claims;
using ArquitecturaBase.Application.Interfaces.Services;
using ArquitecturaBase.Application.Models.Identity;
using Microsoft.IdentityModel.Tokens;
using OpenIddict.Abstractions;
using static OpenIddict.Abstractions.OpenIddictConstants;

namespace ArquitecturaBase.Api.Authentication;

/// <summary>Arma los claims de los tokens OpenID Connect con datos vigentes del usuario.</summary>
public sealed class OpenIdPrincipalFactory(IConnectService service, IOpenIddictScopeManager scopeManager)
{
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

    public async Task<ClaimsPrincipal?> RefreshAsync(ClaimsPrincipal stored, CancellationToken cancellationToken)
    {
        if (!Guid.TryParse(stored.GetClaim(Claims.Subject), CultureInfo.InvariantCulture, out var userId))
        {
            return null;
        }

        // Conservar scopes y autorización del code/refresh token; refrescar los datos mutables de la cuenta.
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
        var user = await service.GetActiveUserAsync(userId, cancellationToken);
        if (user is null)
        {
            return false;
        }

        var account = user.Account;
        identity
            .SetClaim(Claims.Subject, account.Id.ToString("D", CultureInfo.InvariantCulture))
            // SetClaim con null borra el correo anterior al refrescar un token.
            .SetClaim(Claims.Email, account.Email)
            .SetClaim(Claims.Name, NameOf(account))
            .SetClaims(Claims.Role, [.. user.Roles]);
        return true;
    }

    internal static string? NameOf(UserAccount user) => user.DisplayName ?? user.Email ?? user.PhoneNumber;

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
