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
[WhatsAppRoute(WhatsAppRouteFeature.Forms)]
[HasPermission(WhatsAppPermissions.Read)]
[EnableRateLimiting(WhatsAppApiRegistration.ChatReadRateLimitPolicy)]
[ProducesProblem(StatusCodes.Status404NotFound)]
public sealed class WhatsAppFormsController(IWhatsAppFormService service, IWhatsAppFormSendService sendService, ISystemSettingsService settings) : ControllerBase
{
    [HttpGet("{id:guid}/forms")]
    [HasPermission(WhatsAppPermissions.Send)]
    [ProducesResponseType<WhatsAppFormsResponse>(StatusCodes.Status200OK)]
    [ProducesProblem(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> List([FromRoute] Guid id, [FromQuery] ListWhatsAppFormsQuery query, CancellationToken cancellationToken) =>
        (await service.ListAsync(new(id)
        {
            Page = query.Page ?? PagedRequest.DefaultPage,
            PageSize = query.PageSize ?? (await settings.GetPresentationAsync(cancellationToken)).Value.DefaultPageSize,
            Sort = query.Sort, Search = query.Search,
        }, cancellationToken)).ToActionResult(this);

    [HttpGet("{id:guid}/forms/{definitionId:guid}/versions/{versionId:guid}/preparation")]
    [HasPermission(WhatsAppPermissions.Send)]
    [ProducesResponseType<WhatsAppFormPreparationResponse>(StatusCodes.Status200OK)]
    [ProducesProblem(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Prepare([FromRoute] Guid id, [FromRoute] Guid definitionId, [FromRoute] Guid versionId,
        CancellationToken cancellationToken) =>
        (await service.PrepareAsync(id, definitionId, versionId, cancellationToken)).ToActionResult(this);

    [HttpPost("{id:guid}/form-messages")]
    [HasPermission(WhatsAppPermissions.Send)]
    [EnableRateLimiting(WhatsAppApiRegistration.ChatWriteRateLimitPolicy)]
    [ProducesResponseType<WhatsAppOutgoingMessageResponse>(StatusCodes.Status202Accepted)]
    [ProducesProblem(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Send([FromRoute] Guid id, [FromBody] SendWhatsAppFormHttpRequest request, CancellationToken cancellationToken)
    {
        var result = await sendService.SendAsync(new(id, request.RequestId, request.DefinitionId, request.VersionId,
            request.ExpectedModeRevision, request.ExpectedRecipientContextRevision, request.Message), cancellationToken);
        if (result.IsFailure) return result.ToActionResult(this);
        var response = result.Value with
        {
            RecoveryPath = Url.Action(nameof(WhatsAppChatsController.Outgoing), "WhatsAppChats",
                new { id, requestId = request.RequestId }),
        };
        return Result.Success(response).ToAcceptedResult(this);
    }
}
