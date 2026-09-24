using System.Globalization;
using System.Security.Claims;
using ArquitecturaBase.Application.Models.Auth;
using Microsoft.AspNetCore;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Identity;
using OpenIddict.Abstractions;
using static OpenIddict.Abstractions.OpenIddictConstants;

namespace ArquitecturaBase.Api.Endpoints.Connect;

/// <summary>
/// Emite el authorization code si hay sesión (cookie de Identity). Si no, manda al SPA a /login con el authorize
/// original como returnUrl (sección 5.2); si el SPA renueva en silencio (prompt=none), responde login_required.
/// </summary>
internal sealed class AuthorizeEndpoint : IEndpoint
{
    public void MapEndpoint(IEndpointRouteBuilder app) =>
        app.MapMethods(ReturnUrls.AuthorizePath, [HttpMethods.Get, HttpMethods.Post], HandleAsync)
            .AllowAnonymous()
            .ExcludeFromDescription();

    private static async Task<IResult> HandleAsync(
        HttpContext httpContext,
        OpenIdPrincipalFactory principalFactory,
        CancellationToken cancellationToken)
    {
        var request = httpContext.GetOpenIddictServerRequest()
            ?? throw new InvalidOperationException("The OpenID Connect request cannot be retrieved.");

        var session = await httpContext.AuthenticateAsync(IdentityConstants.ApplicationScheme);
        var principal = session.Succeeded && TryGetUserId(session.Principal, out var userId)
            ? await principalFactory.CreateAsync(userId, request.GetScopes(), cancellationToken)
            : null;

        if (principal is not null)
        {
            return OpenIddictResults.SignIn(principal);
        }

        // Hay cookie pero la cuenta ya no puede ingresar (deshabilitada o borrada): se cierra la sesión.
        if (session.Succeeded)
        {
            await httpContext.SignOutAsync(IdentityConstants.ApplicationScheme);
        }

        if (request.HasPromptValue(PromptValues.None))
        {
            return OpenIddictResults.Forbid(Errors.LoginRequired, "The user is not signed in.");
        }

        return Results.Redirect(ReturnUrls.LoginPath + QueryString.Create("returnUrl", await BuildReturnUrlAsync(httpContext.Request, cancellationToken)));
    }

    // El mismo pedido, siempre como GET: el SPA navega a esta URL después de verificar el código.
    private static async Task<string> BuildReturnUrlAsync(HttpRequest request, CancellationToken cancellationToken)
    {
        var parameters = request.HasFormContentType
            ? (await request.ReadFormAsync(cancellationToken)).ToList()
            : request.Query.ToList();

        return ReturnUrls.AuthorizePath + QueryString.Create(parameters);
    }

    private static bool TryGetUserId(ClaimsPrincipal? principal, out Guid userId) =>
        Guid.TryParse(principal?.FindFirstValue(ClaimTypes.NameIdentifier), CultureInfo.InvariantCulture, out userId);
}
