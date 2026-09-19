using System.Globalization;
using ArquitecturaBase.Application.Abstractions.Identity;
using Microsoft.AspNetCore.Authentication;
using OpenIddict.Abstractions;
using OpenIddict.Server.AspNetCore;
using static OpenIddict.Abstractions.OpenIddictConstants;

namespace ArquitecturaBase.Api.Endpoints.Connect;

/// <summary>Claims del usuario según los scopes del access token, que OpenIddict ya validó.</summary>
internal sealed class UserInfoEndpoint : IEndpoint
{
    public void MapEndpoint(IEndpointRouteBuilder app) =>
        app.MapMethods("/connect/userinfo", [HttpMethods.Get, HttpMethods.Post], HandleAsync)
            .AllowAnonymous()
            .ExcludeFromDescription();

    private static async Task<IResult> HandleAsync(
        HttpContext httpContext,
        IIdentityService identityService,
        CancellationToken cancellationToken)
    {
        var principal = (await httpContext.AuthenticateAsync(OpenIddictServerAspNetCoreDefaults.AuthenticationScheme)).Principal;
        var user = Guid.TryParse(principal?.GetClaim(Claims.Subject), CultureInfo.InvariantCulture, out var userId)
            ? await identityService.FindByIdAsync(userId, cancellationToken)
            : null;

        if (principal is null || user is not { IsActive: true })
        {
            return OpenIddictResults.Challenge(Errors.InvalidToken, "The user no longer exists or is disabled.");
        }

        var claims = new Dictionary<string, object>(StringComparer.Ordinal)
        {
            [Claims.Subject] = user.Id.ToString("D", CultureInfo.InvariantCulture),
        };

        if (principal.HasScope(Scopes.Email))
        {
            claims[Claims.Email] = user.Email;
            claims[Claims.EmailVerified] = true;
        }

        if (principal.HasScope(Scopes.Profile))
        {
            claims[Claims.Name] = user.DisplayName ?? user.Email;
            claims[Claims.Locale] = user.Culture;
            claims[Claims.Zoneinfo] = user.TimeZoneId;
        }

        if (principal.HasScope(Scopes.Roles))
        {
            claims[Claims.Role] = await identityService.GetRolesAsync(user.Id, cancellationToken);
        }

        return Results.Ok(claims);
    }
}
