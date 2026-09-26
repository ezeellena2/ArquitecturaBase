using ArquitecturaBase.Api.Contracts.Auth;
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
    [ProducesResponseType<RequestLoginCodeResponse>(StatusCodes.Status202Accepted)]
    public async Task<IActionResult> RequestLoginCode(
        [FromBody] RequestLoginCodeHttpRequest request,
        CancellationToken cancellationToken) =>
        (await service.RequestLoginCodeAsync(new RequestLoginCodeRequest(request.Email), cancellationToken))
            .ToAcceptedResult(this);

    [HttpPost("whatsapp")]
    [WhatsAppRoute(WhatsAppRouteFeature.Messaging)]
    [AllowAnonymous]
    [EnableRateLimiting(RateLimitingExtensions.LoginCodePolicy)]
    [ProducesResponseType<RequestWhatsAppLoginCodeResponse>(StatusCodes.Status202Accepted)]
    public async Task<IActionResult> RequestWhatsAppLoginCode(
        [FromBody] RequestWhatsAppLoginCodeHttpRequest request,
        CancellationToken cancellationToken) =>
        (await service.RequestWhatsAppLoginCodeAsync(
            new RequestWhatsAppLoginCodeRequest(request.Country, request.Number), cancellationToken))
            .ToAcceptedResult(this);

    [HttpPost("verify")]
    [AllowAnonymous]
    [EnableRateLimiting(RateLimitingExtensions.LoginVerifyPolicy)]
    [ProducesResponseType<VerifyLoginCodeResponse>(StatusCodes.Status200OK)]
    public async Task<IActionResult> VerifyLoginCode(
        [FromBody] VerifyLoginCodeHttpRequest request,
        CancellationToken cancellationToken) =>
        (await service.VerifyLoginCodeAsync(
            new VerifyLoginCodeRequest(request.Email, request.Code, request.ReturnUrl, request.Phone),
            cancellationToken)).ToActionResult(this);
}
