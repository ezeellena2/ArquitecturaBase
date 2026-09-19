using System.Security.Claims;
using Microsoft.AspNetCore.Authentication;
using OpenIddict.Server.AspNetCore;

namespace ArquitecturaBase.Api.Endpoints.Connect;

/// <summary>
/// Respuestas que arma OpenIddict: redirección con el code o los tokens, o un error OAuth. Son Results y no
/// TypedResults: con TypedResults, .NET 10 convierte las redirecciones de autenticación en 401.
/// </summary>
internal static class OpenIddictResults
{
    public static IResult SignIn(ClaimsPrincipal principal) =>
        Results.SignIn(principal, authenticationScheme: OpenIddictServerAspNetCoreDefaults.AuthenticationScheme);

    public static IResult Forbid(string error, string description) =>
        Results.Forbid(ErrorProperties(error, description), [OpenIddictServerAspNetCoreDefaults.AuthenticationScheme]);

    public static IResult Challenge(string error, string description) =>
        Results.Challenge(ErrorProperties(error, description), [OpenIddictServerAspNetCoreDefaults.AuthenticationScheme]);

    private static AuthenticationProperties ErrorProperties(string error, string description) =>
        new(new Dictionary<string, string?>
        {
            [OpenIddictServerAspNetCoreConstants.Properties.Error] = error,
            [OpenIddictServerAspNetCoreConstants.Properties.ErrorDescription] = description,
        });
}
