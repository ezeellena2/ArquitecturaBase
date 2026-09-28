using ArquitecturaBase.Api.Contracts.Users;
using ArquitecturaBase.Api.ErrorHandling;
using ArquitecturaBase.Api.OpenApi;
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
// Todas leen la cuenta de la sesión: si se borró con el token todavía vigente, responden 404.
[ProducesProblem(StatusCodes.Status404NotFound)]
public sealed class MeController(
    IProfileQueryService queries,
    IProfileService profile,
    IProfileWhatsAppService whatsApp) : ControllerBase
{
    [HttpGet]
    [ProducesResponseType<CurrentUserResponse>(StatusCodes.Status200OK)]
    public async Task<IActionResult> Get(CancellationToken cancellationToken) =>
        (await queries.GetAsync(cancellationToken)).ToActionResult(this);

    [HttpPut]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> Update(
        [FromBody] UpdateProfileHttpRequest request,
        CancellationToken cancellationToken) =>
        (await profile.UpdateAsync(
            new UpdateProfileRequest(request.DisplayName, request.Culture, request.TimeZoneId), cancellationToken))
            .ToActionResult(this);

    [HttpPost("email/code")]
    [EnableRateLimiting(RateLimitingExtensions.LoginCodePolicy)]
    [ProducesResponseType<RequestEmailCodeResponse>(StatusCodes.Status202Accepted)]
    public async Task<IActionResult> RequestEmailCode(
        [FromBody] RequestEmailCodeHttpRequest request,
        CancellationToken cancellationToken) =>
        (await profile.RequestEmailCodeAsync(new RequestEmailCodeRequest(request.Email), cancellationToken))
            .ToAcceptedResult(this);

    [HttpPut("email")]
    [EnableRateLimiting(RateLimitingExtensions.LoginVerifyPolicy)]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    // El correo ya es de otra cuenta, y se dice recién después de un código correcto.
    [ProducesProblem(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> ConfirmEmail(
        [FromBody] ConfirmEmailHttpRequest request,
        CancellationToken cancellationToken) =>
        (await profile.ConfirmEmailAsync(new ConfirmEmailRequest(request.Email, request.Code), cancellationToken))
            .ToActionResult(this);

    [HttpPost("whatsapp/code")]
    [WhatsAppRoute(WhatsAppRouteFeature.Messaging)]
    [EnableRateLimiting(RateLimitingExtensions.LoginCodePolicy)]
    [ProducesResponseType<RequestPhoneLinkCodeResponse>(StatusCodes.Status202Accepted)]
    public async Task<IActionResult> RequestPhoneLinkCode(
        [FromBody] RequestPhoneLinkCodeHttpRequest request,
        CancellationToken cancellationToken) =>
        (await whatsApp.RequestPhoneLinkCodeAsync(
            new RequestPhoneLinkCodeRequest(request.Country, request.Number), cancellationToken))
            .ToAcceptedResult(this);

    [HttpPut("whatsapp")]
    [EnableRateLimiting(RateLimitingExtensions.LoginVerifyPolicy)]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    // El número ya es de otra cuenta, y se dice recién después de un código correcto.
    [ProducesProblem(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> ConfirmPhoneLink(
        [FromBody] ConfirmPhoneLinkHttpRequest request,
        CancellationToken cancellationToken) =>
        (await whatsApp.ConfirmPhoneLinkAsync(
            new ConfirmPhoneLinkRequest(request.Phone, request.Code), cancellationToken))
            .ToActionResult(this);

    [HttpDelete("whatsapp")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    // Es el único medio de ingreso de la cuenta.
    [ProducesProblem(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> UnlinkOwnPhone(CancellationToken cancellationToken) =>
        (await whatsApp.UnlinkOwnPhoneAsync(cancellationToken)).ToActionResult(this);
}
