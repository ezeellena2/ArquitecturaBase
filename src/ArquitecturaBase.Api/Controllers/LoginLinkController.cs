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
    public async Task<IActionResult> Preview(
        [FromBody] PreviewLoginLinkRequest request,
        CancellationToken cancellationToken) =>
        (await service.PreviewAsync(request, cancellationToken)).ToActionResult(this);
}
