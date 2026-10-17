using ArquitecturaBase.Api.Authorization;
using ArquitecturaBase.Api.ErrorHandling;
using ArquitecturaBase.Api.Modules.WhatsApp.Contracts;
using ArquitecturaBase.Api.Modules.WhatsApp.Routing;
using ArquitecturaBase.Api.OpenApi;
using ArquitecturaBase.Application.Modules.WhatsApp.Interfaces.Services;
using ArquitecturaBase.Application.Modules.WhatsApp.Models;
using ArquitecturaBase.Domain.Modules.WhatsApp;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace ArquitecturaBase.Api.Modules.WhatsApp.Controllers;

[ApiController]
[Route("api/whatsapp/chats/{id:guid}/assistant-suggestions")]
[Tags("WhatsApp chats")]
[WhatsAppRoute(WhatsAppRouteFeature.Forms)]
[HasPermission(WhatsAppPermissions.Read)]
[HasPermission(WhatsAppPermissions.Send)]
[EnableRateLimiting(WhatsAppApiRegistration.ChatReadRateLimitPolicy)]
[ProducesProblem(StatusCodes.Status404NotFound)]
[ProducesProblem(StatusCodes.Status409Conflict)]
public sealed class WhatsAppAssistantSuggestionsController(IWhatsAppAssistantService service) : ControllerBase
{
    [HttpPost]
    [EnableRateLimiting(WhatsAppApiRegistration.ChatWriteRateLimitPolicy)]
    [ProducesResponseType<WhatsAppAssistantSuggestionResponse>(StatusCodes.Status202Accepted)]
    public async Task<IActionResult> RequestSuggestion([FromRoute] Guid id, [FromBody] RequestWhatsAppAssistantSuggestionHttpRequest request,
        CancellationToken cancellationToken) =>
        (await service.RequestAsync(new(id, request.RequestId, request.SourceMessageId, request.ExpectedModeRevision), cancellationToken)).ToAcceptedResult(this);

    [HttpGet("{requestId:guid}")]
    [ProducesResponseType<WhatsAppAssistantSuggestionResponse>(StatusCodes.Status200OK)]
    public async Task<IActionResult> Get([FromRoute] Guid id, [FromRoute] Guid requestId, CancellationToken cancellationToken) =>
        (await service.GetAsync(id, requestId, cancellationToken)).ToActionResult(this);

    [HttpPost("{requestId:guid}/discard")]
    [EnableRateLimiting(WhatsAppApiRegistration.ChatWriteRateLimitPolicy)]
    [ProducesResponseType<WhatsAppAssistantSuggestionResponse>(StatusCodes.Status200OK)]
    public async Task<IActionResult> Discard([FromRoute] Guid id, [FromRoute] Guid requestId, CancellationToken cancellationToken) =>
        (await service.DiscardAsync(id, requestId, cancellationToken)).ToActionResult(this);

    [HttpPost("{requestId:guid}/send")]
    [EnableRateLimiting(WhatsAppApiRegistration.ChatWriteRateLimitPolicy)]
    [ProducesResponseType<WhatsAppOutgoingMessageResponse>(StatusCodes.Status202Accepted)]
    public async Task<IActionResult> Send([FromRoute] Guid id, [FromRoute] Guid requestId, [FromBody] SendWhatsAppAssistantSuggestionHttpRequest request,
        CancellationToken cancellationToken) =>
        (await service.SendAsync(new(id, requestId, request.RequestId, request.IncludedFieldIds), cancellationToken)).ToAcceptedResult(this);
}
