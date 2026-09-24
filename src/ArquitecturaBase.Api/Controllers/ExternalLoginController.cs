using ArquitecturaBase.Api.Contracts.Auth;
using ArquitecturaBase.Api.ErrorHandling;
using ArquitecturaBase.Application.Interfaces.Services;
using ArquitecturaBase.Application.Models.Auth;
using ArquitecturaBase.Application.Resources;
using ArquitecturaBase.Domain.Results;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Google;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace ArquitecturaBase.Api.Controllers;

/// <summary>Ingreso con Google. Las respuestas son navegaciones del navegador.</summary>
[ApiController]
[Route("account/external")]
[Tags("Account")]
public sealed class ExternalLoginController(IExternalLoginService service, IAuthenticationSchemeProvider schemes) : ControllerBase
{
    private const string CallbackPath = "/account/external/callback";
    private const string LoginProviderKey = "LoginProvider";

    [HttpGet("google")]
    [AllowAnonymous]
    public async Task<IActionResult> Google([FromQuery] ExternalLoginQuery query)
    {
        // Sin ClientId configurado, Google no se registra.
        if (await schemes.GetSchemeAsync(GoogleDefaults.AuthenticationScheme) is null)
        {
            return NotFound();
        }

        if (!ReturnUrls.IsAuthorizeRequest(query.ReturnUrl))
        {
            Result invalid = new ValidationError(new Dictionary<string, string[]>
            {
                ["returnUrl"] = [ValidationMessages.ReturnUrlInvalid],
            });
            return invalid.ToActionResult(this);
        }

        var properties = new AuthenticationProperties
        {
            RedirectUri = CallbackPath + QueryString.Create("returnUrl", query.ReturnUrl),
        };
        properties.Items[LoginProviderKey] = GoogleDefaults.AuthenticationScheme;

        return Challenge(properties, GoogleDefaults.AuthenticationScheme);
    }

    [HttpGet("callback")]
    [AllowAnonymous]
    public async Task<IActionResult> Callback([FromQuery] ExternalLoginQuery query, CancellationToken cancellationToken)
    {
        var result = await service.SignInAsync(new ExternalSignInRequest(query.ReturnUrl), cancellationToken);

        return result.IsSuccess
            ? LocalRedirect(result.Value.ReturnUrl)
            : Redirect(ReturnUrls.LoginPath + QueryString.Create("error", result.Error.Code));
    }
}
