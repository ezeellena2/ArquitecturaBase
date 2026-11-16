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
/// El número de WhatsApp del perfil, dentro de <c>/api/me</c>: cómo está su chat, pedir el código y confirmarlo, y pedir
/// el código que confirma agregar un correo. Tiene
/// los mismos atributos que el resto del perfil (<c>MeController</c>); leer el chat y pedir el código dependen de que
/// WhatsApp esté prendido.
/// </summary>
[ApiController]
[Route("api/me/whatsapp")]
[Tags("Users")]
[Authorize]
// Todas leen la cuenta de la sesión: si se borró con el token todavía vigente, responden 404.
[ProducesProblem(StatusCodes.Status404NotFound)]
public sealed class MeWhatsAppController(IProfileWhatsAppService whatsApp, IProfileWhatsAppChatService chat) : ControllerBase
{
    /// <summary>Si el chat del número está vinculado a la cuenta y hasta cuándo le pueden llegar los avisos.</summary>
    [HttpGet]
    [WhatsAppRoute(WhatsAppRouteFeature.Messaging)]
    [ProducesResponseType<ProfileWhatsAppChatResponse>(StatusCodes.Status200OK)]
    public async Task<IActionResult> GetChat(CancellationToken cancellationToken) =>
        (await chat.GetChatAsync(cancellationToken)).ToActionResult(this);

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

    /// <summary>
    /// El código, al número verificado de la cuenta, que confirma agregar un correo (cada medio confirma los cambios del
    /// otro): va como <c>phoneCode</c> en <c>PUT /api/me/email</c>.
    /// </summary>
    [HttpPost("confirmation-code")]
    [WhatsAppRoute(WhatsAppRouteFeature.Messaging)]
    [EnableRateLimiting(RateLimitingExtensions.LoginCodePolicy)]
    [ProducesResponseType<RequestPhoneConfirmationCodeResponse>(StatusCodes.Status202Accepted)]
    // Sin número verificado, o con un correo verificado, que no se cambia.
    [ProducesProblem(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> RequestPhoneConfirmationCode(CancellationToken cancellationToken) =>
        (await whatsApp.RequestPhoneConfirmationCodeAsync(cancellationToken)).ToAcceptedResult(this);

    [HttpPut]
    [EnableRateLimiting(RateLimitingExtensions.LoginVerifyPolicy)]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    // El número ya es de otra cuenta, y se dice recién después de un código correcto; o la cuenta ya tiene un número
    // verificado, que desde el perfil no se reemplaza.
    [ProducesProblem(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> ConfirmPhoneLink(
        [FromBody] ConfirmPhoneLinkHttpRequest request,
        CancellationToken cancellationToken) =>
        (await whatsApp.ConfirmPhoneLinkAsync(
            new ConfirmPhoneLinkRequest(request.Phone, request.Code, request.EmailCode), cancellationToken))
            .ToActionResult(this);
}
