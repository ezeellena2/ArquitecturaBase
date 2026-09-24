using ArquitecturaBase.Application.Models.Auth;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Identity;
using OpenIddict.Abstractions;
using OpenIddict.Server.AspNetCore;

namespace ArquitecturaBase.Api.Endpoints.Connect;

/// <summary>Cierra la cookie, revoca los tokens de la autorización del SPA y vuelve a /login (sección 5.5).</summary>
internal sealed class LogoutEndpoint : IEndpoint
{
    public void MapEndpoint(IEndpointRouteBuilder app) =>
        app.MapMethods("/connect/logout", [HttpMethods.Get, HttpMethods.Post], HandleAsync)
            .AllowAnonymous()
            .ExcludeFromDescription();

    private static async Task<IResult> HandleAsync(
        HttpContext httpContext,
        IOpenIddictTokenManager tokenManager,
        CancellationToken cancellationToken)
    {
        // El id_token_hint identifica la autorización: se revocan su refresh token y sus access tokens.
        var hint = await httpContext.AuthenticateAsync(OpenIddictServerAspNetCoreDefaults.AuthenticationScheme);
        var authorizationId = hint.Principal?.GetAuthorizationId();

        if (!string.IsNullOrEmpty(authorizationId))
        {
            await tokenManager.RevokeByAuthorizationIdAsync(authorizationId, cancellationToken);
        }

        await httpContext.SignOutAsync(IdentityConstants.ApplicationScheme);

        // OpenIddict redirige al post_logout_redirect_uri del cliente; si no vino ninguno, a /login.
        return Results.SignOut(
            new AuthenticationProperties { RedirectUri = ReturnUrls.LoginPath },
            [OpenIddictServerAspNetCoreDefaults.AuthenticationScheme]);
    }
}
