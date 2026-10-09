using System.Globalization;
using ArquitecturaBase.Api.Authorization;
using ArquitecturaBase.Api.ErrorHandling;
using ArquitecturaBase.Api.Modules.ArtificialIntelligence.Contracts.Assistant;
using ArquitecturaBase.Api.OpenApi;
using ArquitecturaBase.Application.Common.Pagination;
using ArquitecturaBase.Application.Interfaces.Services;
using ArquitecturaBase.Application.Modules.ArtificialIntelligence.Interfaces.Services;
using ArquitecturaBase.Application.Modules.ArtificialIntelligence.Models;
using ArquitecturaBase.Domain.Modules.ArtificialIntelligence;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace ArquitecturaBase.Api.Modules.ArtificialIntelligence.Controllers;

[ApiController]
[Route("api/ai/assistant")]
[Tags("AI assistant")]
[HasPermission(ArtificialIntelligencePermissions.AssistantUse)]
[EnableRateLimiting(ArtificialIntelligenceApiRegistration.ReadPolicy)]
[RequestSizeLimit(16384)]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public sealed class AiAssistantController(IAiAssistantService service, ISystemSettingsService settings) : ControllerBase
{
    [HttpGet("bootstrap")]
    [ProducesResponseType<AiAssistantBootstrapResponse>(StatusCodes.Status200OK)]
    public async Task<IActionResult> Bootstrap(CancellationToken cancellationToken) =>
        (await service.BootstrapAsync(cancellationToken)).ToActionResult(this);

    [HttpGet("conversations")]
    [ProducesResponseType<PagedResult<AiConversationSummaryResponse>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> ListConversations([FromQuery] ListAiConversationsQuery query, CancellationToken cancellationToken) =>
        (await service.ListConversationsAsync(new ListAiConversationsRequest
        {
            Page = query.Page ?? PagedRequest.DefaultPage,
            PageSize = query.PageSize ?? (await settings.GetPresentationAsync(cancellationToken)).Value.DefaultPageSize,
            Sort = query.Sort,
        }, cancellationToken)).ToActionResult(this);

    [HttpGet("conversations/{conversationId:guid}")]
    [ProducesResponseType<AiConversationResponse>(StatusCodes.Status200OK)]
    public async Task<IActionResult> GetConversation([FromRoute] Guid conversationId, CancellationToken cancellationToken) =>
        (await service.GetConversationAsync(conversationId, cancellationToken)).ToActionResult(this);

    [HttpGet("conversations/{conversationId:guid}/turns")]
    [ProducesResponseType<PagedResult<AiAssistantTurnResponse>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> ListTurns([FromRoute] Guid conversationId, [FromQuery] ListAiAssistantTurnsQuery query, CancellationToken cancellationToken) =>
        (await service.ListTurnsAsync(conversationId, new ListAiAssistantTurnsRequest
        {
            Page = query.Page ?? PagedRequest.DefaultPage,
            PageSize = query.PageSize ?? (await settings.GetPresentationAsync(cancellationToken)).Value.DefaultPageSize,
            Sort = query.Sort,
        }, cancellationToken)).ToActionResult(this);

    [HttpPost("turns")]
    [EnableRateLimiting(ArtificialIntelligenceApiRegistration.AssistantSubmitPolicy)]
    [ProducesResponseType<AiAssistantTurnAcceptedResponse>(StatusCodes.Status202Accepted)]
    [ProducesProblem(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Submit([FromBody] SubmitAiAssistantTurnHttpRequest request, CancellationToken cancellationToken) =>
        (await service.SubmitAsync(new(request.RequestId, request.ConversationId, request.ExpectedRevision,
            request.ExpectedConnectionRevision, request.Question, CultureInfo.CurrentUICulture.TwoLetterISOLanguageName), cancellationToken)).ToAcceptedResult(this);

    [HttpGet("turns/{requestId:guid}")]
    [ProducesResponseType<AiAssistantTurnResponse>(StatusCodes.Status200OK)]
    public async Task<IActionResult> GetTurn([FromRoute] Guid requestId, CancellationToken cancellationToken) =>
        (await service.GetTurnAsync(requestId, cancellationToken)).ToActionResult(this);

    [HttpPost("turns/{requestId:guid}/cancel")]
    [EnableRateLimiting(ArtificialIntelligenceApiRegistration.WritePolicy)]
    [ProducesResponseType<AiAssistantTurnResponse>(StatusCodes.Status200OK)]
    public async Task<IActionResult> Cancel([FromRoute] Guid requestId, CancellationToken cancellationToken) =>
        (await service.CancelAsync(new(requestId), cancellationToken)).ToActionResult(this);

    [HttpDelete("conversations/{conversationId:guid}")]
    [EnableRateLimiting(ArtificialIntelligenceApiRegistration.WritePolicy)]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> RemoveConversation([FromRoute] Guid conversationId, CancellationToken cancellationToken) =>
        (await service.RemoveConversationAsync(conversationId, cancellationToken)).ToActionResult(this);
}
