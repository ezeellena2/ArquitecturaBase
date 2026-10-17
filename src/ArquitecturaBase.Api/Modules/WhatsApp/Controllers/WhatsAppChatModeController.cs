using ArquitecturaBase.Api.ErrorHandling;
using ArquitecturaBase.Api.Modules.WhatsApp.Contracts;
using ArquitecturaBase.Api.Modules.WhatsApp.Routing;
using ArquitecturaBase.Api.OpenApi;
using ArquitecturaBase.Application.Modules.WhatsApp.Interfaces.Services;
using ArquitecturaBase.Application.Modules.WhatsApp.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace ArquitecturaBase.Api.Modules.WhatsApp.Controllers;

[ApiController]
[Route("api/whatsapp/chats")]
[Tags("WhatsApp chats")]
[WhatsAppRoute(WhatsAppRouteFeature.Messaging)]
[Authorize]
[EnableRateLimiting(WhatsAppApiRegistration.ChatWriteRateLimitPolicy)]
public sealed class WhatsAppChatModeController(IWhatsAppChatModeService service) : ControllerBase
{
    [HttpPut("{id:guid}/mode")]
    [ProducesResponseType<WhatsAppModeChangedResponse>(StatusCodes.Status200OK)]
    [ProducesProblem(StatusCodes.Status404NotFound)]
    [ProducesProblem(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Change([FromRoute] Guid id, [FromBody] ChangeWhatsAppModeHttpRequest request, CancellationToken cancellationToken) =>
        (await service.ChangeAsync(new(id, request.Mode, request.ExpectedRevision), cancellationToken)).ToActionResult(this);
}
