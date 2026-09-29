using ArquitecturaBase.Api.ErrorHandling;
using ArquitecturaBase.Api.Modules.WhatsApp.Contracts;
using ArquitecturaBase.Api.Modules.WhatsApp.Routing;
using ArquitecturaBase.Api.OpenApi;
using ArquitecturaBase.Api.RateLimiting;
using ArquitecturaBase.Application.Modules.WhatsApp.Interfaces.Services;
using ArquitecturaBase.Application.Modules.WhatsApp.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace ArquitecturaBase.Api.Modules.WhatsApp.Controllers;

/// <summary>
/// El número de WhatsApp del perfil, dentro de <c>/api/me</c>: pedir el código, confirmarlo y soltar el número. Tiene los
/// mismos atributos que el resto del perfil (<c>MeController</c>); solo el pedido del código depende de que WhatsApp
/// esté prendido.
/// </summary>
[ApiController]
[Route("api/me/whatsapp")]
[Tags("Users")]
[Authorize]
// Todas leen la cuenta de la sesión: si se borró con el token todavía vigente, responden 404.
[ProducesProblem(StatusCodes.Status404NotFound)]
public sealed class MeWhatsAppController(IProfileWhatsAppService whatsApp) : ControllerBase
{
    [HttpPost("code")]
    [WhatsAppRoute(WhatsAppRouteFeature.Messaging)]
    [EnableRateLimiting(RateLimitingExtensions.LoginCodePolicy)]
    [ProducesResponseType<RequestPhoneLinkCodeResponse>(StatusCodes.Status202Accepted)]
    public async Task<IActionResult> RequestPhoneLinkCode(
        [FromBody] RequestPhoneLinkCodeHttpRequest request,
        CancellationToken cancellationToken) =>
        (await whatsApp.RequestPhoneLinkCodeAsync(
            new RequestPhoneLinkCodeRequest(request.Country, request.Number), cancellationToken))
            .ToAcceptedResult(this);

    [HttpPut]
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

    [HttpDelete]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    // Es el único medio de ingreso de la cuenta.
    [ProducesProblem(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> UnlinkOwnPhone(CancellationToken cancellationToken) =>
        (await whatsApp.UnlinkOwnPhoneAsync(cancellationToken)).ToActionResult(this);
}
