using ArquitecturaBase.Api.Authorization;
using ArquitecturaBase.Api.ErrorHandling;
using ArquitecturaBase.Api.Modules.WhatsApp.Contracts;
using ArquitecturaBase.Api.Modules.WhatsApp.Routing;
using ArquitecturaBase.Api.OpenApi;
using ArquitecturaBase.Application.Common.Pagination;
using ArquitecturaBase.Application.Interfaces.Services;
using ArquitecturaBase.Application.Modules.WhatsApp.Interfaces.Services;
using ArquitecturaBase.Application.Modules.WhatsApp.Models;
using ArquitecturaBase.Domain.Modules.WhatsApp;
using ArquitecturaBase.Domain.Results;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace ArquitecturaBase.Api.Modules.WhatsApp.Controllers;

[ApiController]
[Route("api/whatsapp/chats")]
[Tags("WhatsApp chats")]
[WhatsAppRoute(WhatsAppRouteFeature.Messaging)]
[HasPermission(WhatsAppPermissions.Read)]
[EnableRateLimiting(WhatsAppApiRegistration.ChatReadRateLimitPolicy)]
[ProducesProblem(StatusCodes.Status404NotFound)]
public sealed class WhatsAppChatsController(IWhatsAppChatService service, ISystemSettingsService settings) : ControllerBase
{
    [HttpGet]
    [ProducesResponseType<WhatsAppChatsResponse>(StatusCodes.Status200OK)]
    public async Task<IActionResult> List([FromQuery] ListWhatsAppChatsQuery query, CancellationToken cancellationToken) =>
        (await service.ListAsync(new ListWhatsAppChatsRequest
        {
            Page = query.Page ?? PagedRequest.DefaultPage,
            PageSize = query.PageSize ?? (await settings.GetPresentationAsync(cancellationToken)).Value.DefaultPageSize,
            Sort = query.Sort, Search = query.Search, UnreadOnly = query.UnreadOnly,
        }, cancellationToken)).ToActionResult(this);

    [HttpGet("{id:guid}/messages")]
    [ProducesResponseType<WhatsAppThreadResponse>(StatusCodes.Status200OK)]
    public async Task<IActionResult> Thread([FromRoute] Guid id, [FromQuery] ListWhatsAppMessagesQuery query, CancellationToken cancellationToken) =>
        (await service.GetThreadAsync(new(id, query.BeforeAtUtc, query.BeforeId, query.Limit), cancellationToken)).ToActionResult(this);

    [HttpPut("{id:guid}/read")]
    [EnableRateLimiting(WhatsAppApiRegistration.ChatWriteRateLimitPolicy)]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> Read([FromRoute] Guid id, [FromBody] MarkWhatsAppChatReadHttpRequest request, CancellationToken cancellationToken) =>
        (await service.MarkReadAsync(new(id, request.ThroughSequence), cancellationToken)).ToActionResult(this);

    [HttpPost("{id:guid}/messages")]
    [HasPermission(WhatsAppPermissions.Send)]
    [EnableRateLimiting(WhatsAppApiRegistration.ChatWriteRateLimitPolicy)]
    [ProducesResponseType<WhatsAppOutgoingMessageResponse>(StatusCodes.Status202Accepted)]
    [ProducesProblem(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Send([FromRoute] Guid id, [FromBody] SendWhatsAppTextHttpRequest request, CancellationToken cancellationToken) =>
        (await service.SendTextAsync(new(id, request.RequestId, request.Body), cancellationToken)).ToAcceptedResult(this);

    [HttpGet("{id:guid}/outgoing/{requestId:guid}")]
    [ProducesResponseType<WhatsAppOutgoingMessageResponse>(StatusCodes.Status200OK)]
    public async Task<IActionResult> Outgoing([FromRoute] Guid id, [FromRoute] Guid requestId, CancellationToken cancellationToken)
    {
        var result = await service.FindOutgoingAsync(id, requestId, cancellationToken);
        if (result.IsFailure) return result.ToActionResult(this);
        return Result.Success(result.Value with
        {
            RecoveryPath = Url.Action(nameof(Outgoing), "WhatsAppChats", new { id, requestId }),
        }).ToActionResult(this);
    }

    [HttpPost("{id:guid}/outgoing/{requestId:guid}/review")]
    [HasPermission(WhatsAppPermissions.Send)]
    [EnableRateLimiting(WhatsAppApiRegistration.ChatWriteRateLimitPolicy)]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesProblem(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Review([FromRoute] Guid id, [FromRoute] Guid requestId, CancellationToken cancellationToken) =>
        (await service.ReviewUncertainAsync(id, requestId, cancellationToken)).ToActionResult(this);
}
