using ArquitecturaBase.Api.Authorization;
using ArquitecturaBase.Api.ErrorHandling;
using ArquitecturaBase.Api.Modules.BusinessForms.Contracts;
using ArquitecturaBase.Api.OpenApi;
using ArquitecturaBase.Application.Common.Pagination;
using ArquitecturaBase.Application.Modules.BusinessForms.Interfaces.Services;
using ArquitecturaBase.Application.Modules.BusinessForms.Models;
using ArquitecturaBase.Domain.Modules.BusinessForms;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
namespace ArquitecturaBase.Api.Modules.BusinessForms.Controllers;

[ApiController]
[Route("api/forms/approvals")]
[Tags("Form approvals")]
[HasPermission(BusinessFormsPermissions.ApprovalsDecide)]
[EnableRateLimiting(BusinessFormsApiRegistration.ReadPolicy)]
[RequestSizeLimit(8192)]
public sealed class FormApprovalsController(IFormApprovalService service) : ControllerBase
{
    [HttpGet]
    [ProducesResponseType<PagedResult<FormApprovalTaskResponse>>(StatusCodes.Status200OK)]
    [ProducesProblem(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> List([FromQuery] FormApprovalsQuery query, CancellationToken cancellationToken)
    {
        Response.Headers.CacheControl = "no-store";
        return (await service.ListAsync(new() { Page = query.Page, PageSize = query.PageSize, DefinitionId = query.DefinitionId, OnlyMine = query.OnlyMine }, cancellationToken)).ToActionResult(this);
    }
    [HttpGet("count")]
    [ProducesResponseType<FormApprovalCountResponse>(StatusCodes.Status200OK)]
    [ProducesProblem(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> Count([FromQuery] FormApprovalFilterQuery query, CancellationToken cancellationToken)
    {
        Response.Headers.CacheControl = "no-store";
        return (await service.CountAsync(new(query.DefinitionId, query.OnlyMine), cancellationToken)).ToActionResult(this);
    }
    [HttpGet("options")]
    [ProducesResponseType<FormApprovalOptionsResponse>(StatusCodes.Status200OK)]
    public async Task<IActionResult> Options([FromQuery] FormApprovalOptionsQuery query, CancellationToken cancellationToken)
    {
        Response.Headers.CacheControl = "no-store";
        return (await service.GetOptionsAsync(query.OnlyMine, cancellationToken)).ToActionResult(this);
    }
    [HttpGet("{id:guid}")]
    [ProducesResponseType<FormApprovalTaskResponse>(StatusCodes.Status200OK)]
    [ProducesProblem(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Get([FromRoute] Guid id, CancellationToken cancellationToken)
    {
        Response.Headers.CacheControl = "no-store";
        return (await service.GetAsync(id, cancellationToken)).ToActionResult(this);
    }
    [HttpPost("{id:guid}/decisions")]
    [EnableRateLimiting(BusinessFormsApiRegistration.WritePolicy)]
    [ProducesResponseType<FormApprovalDecisionResponse>(StatusCodes.Status200OK)]
    [ProducesProblem(StatusCodes.Status400BadRequest)]
    [ProducesProblem(StatusCodes.Status404NotFound)]
    [ProducesProblem(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Decide([FromRoute] Guid id, [FromBody] DecideFormApprovalHttpRequest request, CancellationToken cancellationToken)
    {
        Response.Headers.CacheControl = "no-store";
        return (await service.DecideAsync(id, new(request.ExpectedRevision, request.Decision, request.Reason), cancellationToken)).ToActionResult(this);
    }
}
