using ArquitecturaBase.Api.Contracts.Users;
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
        [FromBody] UpdateProfileHttpRequest request,
        CancellationToken cancellationToken) =>
        (await service.UpdateAsync(
            new UpdateProfileRequest(request.DisplayName, request.Culture, request.TimeZoneId), cancellationToken))
            .ToActionResult(this);

    [HttpPost("email/code")]
    [EnableRateLimiting(RateLimitingExtensions.LoginCodePolicy)]
    [ProducesResponseType(typeof(RequestEmailCodeResponse), StatusCodes.Status202Accepted)]
    public async Task<IActionResult> RequestEmailCode(
        [FromBody] RequestEmailCodeHttpRequest request,
        CancellationToken cancellationToken) =>
        (await service.RequestEmailCodeAsync(new RequestEmailCodeRequest(request.Email), cancellationToken))
            .ToAcceptedResult(this);

    [HttpPut("email")]
    [EnableRateLimiting(RateLimitingExtensions.LoginVerifyPolicy)]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> ConfirmEmail(
        [FromBody] ConfirmEmailHttpRequest request,
        CancellationToken cancellationToken) =>
        (await service.ConfirmEmailAsync(new ConfirmEmailRequest(request.Email, request.Code), cancellationToken))
            .ToActionResult(this);

    [HttpPost("whatsapp/code")]
    [WhatsAppRoute(WhatsAppRouteFeature.Messaging)]
    [EnableRateLimiting(RateLimitingExtensions.LoginCodePolicy)]
    [ProducesResponseType(typeof(RequestPhoneLinkCodeResponse), StatusCodes.Status202Accepted)]
    public async Task<IActionResult> RequestPhoneLinkCode(
        [FromBody] RequestPhoneLinkCodeHttpRequest request,
        CancellationToken cancellationToken) =>
        (await service.RequestPhoneLinkCodeAsync(
            new RequestPhoneLinkCodeRequest(request.Country, request.Number), cancellationToken))
            .ToAcceptedResult(this);

    [HttpPut("whatsapp")]
    [EnableRateLimiting(RateLimitingExtensions.LoginVerifyPolicy)]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> ConfirmPhoneLink(
        [FromBody] ConfirmPhoneLinkHttpRequest request,
        CancellationToken cancellationToken) =>
        (await service.ConfirmPhoneLinkAsync(
            new ConfirmPhoneLinkRequest(request.Phone, request.Code), cancellationToken))
            .ToActionResult(this);

    [HttpDelete("whatsapp")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> UnlinkOwnPhone(CancellationToken cancellationToken) =>
        (await service.UnlinkOwnPhoneAsync(cancellationToken)).ToActionResult(this);
}
