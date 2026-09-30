using ArquitecturaBase.Api.Contracts.Auth;
using ArquitecturaBase.Api.ErrorHandling;
using ArquitecturaBase.Api.OpenApi;
using ArquitecturaBase.Api.RateLimiting;
using ArquitecturaBase.Application.Interfaces.Services;
using ArquitecturaBase.Application.Models.Auth;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace ArquitecturaBase.Api.Controllers;

[ApiController]
[Route("account/login-code")]
[Tags("Account")]
public sealed class LoginCodeController(ILoginCodeService service) : ControllerBase
{
    [HttpPost]
    [AllowAnonymous]
    [EnableRateLimiting(RateLimitingExtensions.LoginCodePolicy)]
    [ProducesResponseType<RequestLoginCodeResponse>(StatusCodes.Status202Accepted)]
    [ProducesProblem(StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> RequestLoginCode(
        [FromBody] RequestLoginCodeHttpRequest request,
        CancellationToken cancellationToken) =>
        (await service.RequestLoginCodeAsync(new RequestLoginCodeRequest(request.Email), cancellationToken))
            .ToAcceptedResult(this);

    [HttpPost("verify")]
    [AllowAnonymous]
    [EnableRateLimiting(RateLimitingExtensions.LoginVerifyPolicy)]
    [ProducesResponseType<VerifyLoginCodeResponse>(StatusCodes.Status200OK)]
    // Anónima, pero la cuenta puede estar desactivada (Auth.Account.Disabled) o no tener invitación
    // (Auth.Account.NotInvited).
    [ProducesProblem(StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> VerifyLoginCode(
        [FromBody] VerifyLoginCodeHttpRequest request,
        CancellationToken cancellationToken) =>
        (await service.VerifyLoginCodeAsync(
            new VerifyLoginCodeRequest(request.Email, request.Code, request.ReturnUrl, request.Phone, request.Register, request.DisplayName),
            cancellationToken)).ToActionResult(this);
}
