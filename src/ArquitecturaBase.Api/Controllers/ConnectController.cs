using System.Globalization;
using System.Security.Claims;
using ArquitecturaBase.Api.Authentication;
using ArquitecturaBase.Application.Interfaces.Services;
using ArquitecturaBase.Application.Models.Auth;
using Microsoft.AspNetCore;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using OpenIddict.Abstractions;
using OpenIddict.Server.AspNetCore;
using static OpenIddict.Abstractions.OpenIddictConstants;

namespace ArquitecturaBase.Api.Controllers;

/// <summary>Adapta los cuatro flujos OpenID Connect al passthrough MVC de OpenIddict.</summary>
[ApiController]
[AllowAnonymous]
[ApiExplorerSettings(IgnoreApi = true)]
[Route("connect")]
public sealed class ConnectController(IConnectService service, OpenIdPrincipalFactory principalFactory) : ControllerBase
{
    [HttpGet("authorize")]
    [HttpPost("authorize")]
    public async Task<IActionResult> Authorize(CancellationToken cancellationToken)
    {
        var request = HttpContext.GetOpenIddictServerRequest()
            ?? throw new InvalidOperationException("The OpenID Connect request cannot be retrieved.");

        var session = await HttpContext.AuthenticateAsync(IdentityConstants.ApplicationScheme);
        var principal = session.Succeeded &&
            Guid.TryParse(session.Principal?.FindFirstValue(ClaimTypes.NameIdentifier), CultureInfo.InvariantCulture, out var userId)
                ? await principalFactory.CreateAsync(userId, request.GetScopes(), cancellationToken)
                : null;

        if (principal is not null)
        {
            return SignIn(principal, OpenIddictServerAspNetCoreDefaults.AuthenticationScheme);
        }

        if (session.Succeeded)
        {
            await HttpContext.SignOutAsync(IdentityConstants.ApplicationScheme);
        }

        if (request.HasPromptValue(PromptValues.None))
        {
            return Forbid(ErrorProperties(Errors.LoginRequired, "The user is not signed in."),
                OpenIddictServerAspNetCoreDefaults.AuthenticationScheme);
        }

        // El SPA vuelve a authorize mediante GET, también si el pedido original llegó por formulario.
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
            ? null
            : await principalFactory.RefreshAsync(stored.Principal, cancellationToken);
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
            await service.RevokeAuthorizationAsync(authorizationId, cancellationToken);
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
            ? await service.GetActiveUserAsync(userId, cancellationToken)
            : null;
        if (principal is null || user is null)
        {
            return Challenge(ErrorProperties(Errors.InvalidToken, "The user no longer exists or is disabled."),
                OpenIddictServerAspNetCoreDefaults.AuthenticationScheme);
        }

        var account = user.Account;
        var claims = new Dictionary<string, object>(StringComparer.Ordinal)
        {
            [Claims.Subject] = account.Id.ToString("D", CultureInfo.InvariantCulture),
        };
        if (principal.HasScope(Scopes.Email) && account.Email is not null)
        {
            claims[Claims.Email] = account.Email;
            claims[Claims.EmailVerified] = account.EmailConfirmed;
        }
        if (principal.HasScope(Scopes.Profile))
        {
            if (OpenIdPrincipalFactory.NameOf(account) is { } name)
            {
                claims[Claims.Name] = name;
            }
            claims[Claims.Locale] = account.Culture;
            claims[Claims.Zoneinfo] = account.TimeZoneId;
        }
        if (principal.HasScope(Scopes.Roles))
        {
            claims[Claims.Role] = user.Roles;
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
