using Microsoft.AspNetCore;
using Microsoft.AspNetCore.Authentication;
using OpenIddict.Abstractions;
using OpenIddict.Server.AspNetCore;
using static OpenIddict.Abstractions.OpenIddictConstants;

namespace ArquitecturaBase.Api.Endpoints.Connect;

/// <summary>
/// Canje del code y del refresh token. OpenIddict ya validó el cliente, el PKCE y el token; acá se vuelve a mirar al
/// usuario: si lo deshabilitaron o lo borraron, no recibe tokens nuevos.
/// </summary>
internal sealed class TokenEndpoint : IEndpoint
{
    public void MapEndpoint(IEndpointRouteBuilder app) =>
        app.MapPost("/connect/token", HandleAsync).AllowAnonymous().ExcludeFromDescription();

    private static async Task<IResult> HandleAsync(
        HttpContext httpContext,
        OpenIdPrincipalFactory principalFactory,
        CancellationToken cancellationToken)
    {
        var request = httpContext.GetOpenIddictServerRequest()
            ?? throw new InvalidOperationException("The OpenID Connect request cannot be retrieved.");

        // OpenIddict rechaza antes cualquier otro grant: solo están habilitados estos dos.
        if (!request.IsAuthorizationCodeGrantType() && !request.IsRefreshTokenGrantType())
        {
            throw new InvalidOperationException("The grant type is not supported.");
        }

        var stored = await httpContext.AuthenticateAsync(OpenIddictServerAspNetCoreDefaults.AuthenticationScheme);
        var principal = stored.Principal is null ? null : await principalFactory.RefreshAsync(stored.Principal, cancellationToken);

        return principal is null
            ? OpenIddictResults.Forbid(Errors.InvalidGrant, "The user can no longer sign in.")
            : OpenIddictResults.SignIn(principal);
    }
}
