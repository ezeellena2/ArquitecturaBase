using ArquitecturaBase.Api.Contracts.Auth;
using ArquitecturaBase.Api.ErrorHandling;
using ArquitecturaBase.Api.RateLimiting;
using ArquitecturaBase.Application.Interfaces.Services;
using ArquitecturaBase.Application.Models.Auth;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace ArquitecturaBase.Api.Controllers;

[ApiController]
[Route("account/login-link")]
[Tags("Account")]
public sealed class LoginLinkController(ILoginLinkService service) : ControllerBase
{
    [HttpPost("preview")]
    [AllowAnonymous]
    [EnableRateLimiting(RateLimitingExtensions.LoginVerifyPolicy)]
    [ProducesResponseType<LoginLinkPreviewResponse>(StatusCodes.Status200OK)]
    public async Task<IActionResult> Preview(
        [FromBody] PreviewLoginLinkHttpRequest request,
        CancellationToken cancellationToken) =>
        (await service.PreviewAsync(new PreviewLoginLinkRequest(request.Token), cancellationToken))
            .ToActionResult(this);

    [HttpPost("redeem")]
    [AllowAnonymous]
    [EnableRateLimiting(RateLimitingExtensions.LoginVerifyPolicy)]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> Redeem(
        [FromBody] RedeemLoginLinkHttpRequest request,
        CancellationToken cancellationToken) =>
        (await service.RedeemAsync(new RedeemLoginLinkRequest(request.Token), cancellationToken))
            .ToActionResult(this);
}
