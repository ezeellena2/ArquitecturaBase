using ArquitecturaBase.Api.ErrorHandling;
using ArquitecturaBase.Api.RateLimiting;
using ArquitecturaBase.Api.Routing;
using ArquitecturaBase.Application.Interfaces.Services;
using ArquitecturaBase.Application.Models.Auth;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace ArquitecturaBase.Api.Controllers;

[ApiController]
[Route("account/login-code")]
[Tags("Account")]
public sealed class LoginCodeController(IAccountService service) : ControllerBase
{
    [HttpPost]
    [AllowAnonymous]
    [EnableRateLimiting(RateLimitingExtensions.LoginCodePolicy)]
    [ProducesResponseType(typeof(RequestLoginCodeResponse), StatusCodes.Status202Accepted)]
    public async Task<IActionResult> RequestLoginCode(
        [FromBody] RequestLoginCodeRequest request,
        CancellationToken cancellationToken)
    {
        var result = await service.RequestLoginCodeAsync(request, cancellationToken);

        return result.IsSuccess
            ? Accepted((string?)null, result.Value)
            : result.ToActionResult(this);
    }

    [HttpPost("whatsapp")]
    [WhatsAppRoute(WhatsAppRouteFeature.Messaging)]
    [AllowAnonymous]
    [EnableRateLimiting(RateLimitingExtensions.LoginCodePolicy)]
    [ProducesResponseType(typeof(RequestWhatsAppLoginCodeResponse), StatusCodes.Status202Accepted)]
    public async Task<IActionResult> RequestWhatsAppLoginCode(
        [FromBody] RequestWhatsAppLoginCodeRequest request,
        CancellationToken cancellationToken)
    {
        var result = await service.RequestWhatsAppLoginCodeAsync(request, cancellationToken);

        return result.IsSuccess
            ? Accepted((string?)null, result.Value)
            : result.ToActionResult(this);
    }

    [HttpPost("verify")]
    [AllowAnonymous]
    [EnableRateLimiting(RateLimitingExtensions.LoginVerifyPolicy)]
    [ProducesResponseType(typeof(VerifyLoginCodeResponse), StatusCodes.Status200OK)]
    public async Task<IActionResult> VerifyLoginCode(
        [FromBody] VerifyLoginCodeRequest request,
        CancellationToken cancellationToken) =>
        (await service.VerifyLoginCodeAsync(request, cancellationToken)).ToActionResult(this);
}
