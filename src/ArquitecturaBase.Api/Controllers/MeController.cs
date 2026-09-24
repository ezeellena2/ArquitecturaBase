using ArquitecturaBase.Api.ErrorHandling;
using ArquitecturaBase.Api.RateLimiting;
using ArquitecturaBase.Api.Routing;
using ArquitecturaBase.Application.Interfaces.Services;
using ArquitecturaBase.Application.Models.Users;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace ArquitecturaBase.Api.Controllers;

[ApiController]
[Route("api/me")]
[Tags("Users")]
[Authorize]
public sealed class MeController(IProfileService service) : ControllerBase
{
    [HttpGet]
    [ProducesResponseType(typeof(CurrentUserResponse), StatusCodes.Status200OK)]
    public async Task<IActionResult> Get(CancellationToken cancellationToken) =>
        (await service.GetAsync(cancellationToken)).ToActionResult(this);

    [HttpPut]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> Update(
        [FromBody] UpdateProfileRequest request,
        CancellationToken cancellationToken) =>
        (await service.UpdateAsync(request, cancellationToken)).ToActionResult(this);

    [HttpPost("email/code")]
    [EnableRateLimiting(RateLimitingExtensions.LoginCodePolicy)]
    [ProducesResponseType(typeof(RequestEmailCodeResponse), StatusCodes.Status202Accepted)]
    public async Task<IActionResult> RequestEmailCode(
        [FromBody] RequestEmailCodeRequest request,
        CancellationToken cancellationToken)
    {
        var result = await service.RequestEmailCodeAsync(request, cancellationToken);
        return result.IsSuccess
            ? Accepted((string?)null, result.Value)
            : result.ToActionResult(this);
    }

    [HttpPut("email")]
    [EnableRateLimiting(RateLimitingExtensions.LoginVerifyPolicy)]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> ConfirmEmail(
        [FromBody] ConfirmEmailRequest request,
        CancellationToken cancellationToken) =>
        (await service.ConfirmEmailAsync(request, cancellationToken)).ToActionResult(this);

    [HttpPost("whatsapp/code")]
    [WhatsAppRoute(WhatsAppRouteFeature.Messaging)]
    [EnableRateLimiting(RateLimitingExtensions.LoginCodePolicy)]
    [ProducesResponseType(typeof(RequestPhoneLinkCodeResponse), StatusCodes.Status202Accepted)]
    public async Task<IActionResult> RequestPhoneLinkCode(
        [FromBody] RequestPhoneLinkCodeRequest request,
        CancellationToken cancellationToken)
    {
        var result = await service.RequestPhoneLinkCodeAsync(request, cancellationToken);
        return result.IsSuccess
            ? Accepted((string?)null, result.Value)
            : result.ToActionResult(this);
    }

    [HttpPut("whatsapp")]
    [EnableRateLimiting(RateLimitingExtensions.LoginVerifyPolicy)]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> ConfirmPhoneLink(
        [FromBody] ConfirmPhoneLinkRequest request,
        CancellationToken cancellationToken) =>
        (await service.ConfirmPhoneLinkAsync(request, cancellationToken)).ToActionResult(this);

    [HttpDelete("whatsapp")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> UnlinkOwnPhone(CancellationToken cancellationToken) =>
        (await service.UnlinkOwnPhoneAsync(cancellationToken)).ToActionResult(this);
}
