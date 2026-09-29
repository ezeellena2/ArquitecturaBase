using ArquitecturaBase.Api.ErrorHandling;
using ArquitecturaBase.Api.Modules.WhatsApp.Contracts;
using ArquitecturaBase.Api.Modules.WhatsApp.Routing;
using ArquitecturaBase.Api.RateLimiting;
using ArquitecturaBase.Application.Interfaces.Services;
using ArquitecturaBase.Application.Modules.WhatsApp.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace ArquitecturaBase.Api.Modules.WhatsApp.Controllers;

/// <summary>
/// El pedido de un código de ingreso por WhatsApp, bajo la ruta del de correo (<c>account/login-code</c>,
/// <c>LoginCodeController</c>), con su mismo límite (<c>login-code</c>) y su 202: la acción conserva la ruta, los
/// atributos y la respuesta que tenía allá. Existe solo con WhatsApp prendido.
/// </summary>
[ApiController]
[Route("account/login-code/whatsapp")]
[Tags("Account")]
public sealed class WhatsAppLoginCodeController(ILoginCodeService service) : ControllerBase
{
    [HttpPost]
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
}
