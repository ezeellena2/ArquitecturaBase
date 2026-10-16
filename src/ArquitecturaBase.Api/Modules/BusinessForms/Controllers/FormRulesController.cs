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
[Route("api/forms/definitions/{id:guid}/rules")]
[Tags("Form rules")]
[HasPermission(BusinessFormsPermissions.RulesManage)]
[EnableRateLimiting(BusinessFormsApiRegistration.ReadPolicy)]
[RequestSizeLimit(4194304)]
public sealed class FormRulesController(IFormRuleService service) : ControllerBase
{
    [HttpGet]
    [ProducesResponseType<FormRuleSetResponse>(StatusCodes.Status200OK)]
    [ProducesProblem(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetCurrent([FromRoute] Guid id, CancellationToken cancellationToken)
    {
        Response.Headers.CacheControl = "no-store";
        return (await service.GetCurrentAsync(id,cancellationToken)).ToActionResult(this);
    }
    [HttpGet("revisions")]
    [ProducesResponseType<PagedResult<FormRuleRevisionResponse>>(StatusCodes.Status200OK)]
    [ProducesProblem(StatusCodes.Status400BadRequest)]
    [ProducesProblem(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> ListRevisions([FromRoute] Guid id, [FromQuery] FormRuleRevisionsQuery query, CancellationToken cancellationToken)
    {
        Response.Headers.CacheControl = "no-store";
        return (await service.ListRevisionsAsync(id,new() { Page = query.Page, PageSize = query.PageSize, Sort = query.Sort },cancellationToken)).ToActionResult(this);
    }
    [HttpGet("revisions/{revision:long}")]
    [ProducesResponseType<FormRuleSetResponse>(StatusCodes.Status200OK)]
    [ProducesProblem(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetRevision([FromRoute] Guid id, [FromRoute] long revision, CancellationToken cancellationToken)
    {
        Response.Headers.CacheControl = "no-store";
        return (await service.GetRevisionAsync(id,revision,cancellationToken)).ToActionResult(this);
    }
    [HttpGet("options")]
    [ProducesResponseType<FormRuleOptionsResponse>(StatusCodes.Status200OK)]
    [ProducesProblem(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetOptions([FromRoute] Guid id, CancellationToken cancellationToken)
    {
        Response.Headers.CacheControl = "no-store";
        return (await service.GetOptionsAsync(id,cancellationToken)).ToActionResult(this);
    }
    [HttpPost]
    [EnableRateLimiting(BusinessFormsApiRegistration.WritePolicy)]
    [ProducesResponseType<FormRuleSetResponse>(StatusCodes.Status201Created)]
    [ProducesProblem(StatusCodes.Status400BadRequest)]
    [ProducesProblem(StatusCodes.Status404NotFound)]
    [ProducesProblem(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Save([FromRoute] Guid id, [FromBody] SaveFormRulesHttpRequest request, CancellationToken cancellationToken)
    {
        Response.Headers.CacheControl = "no-store";
        var mapped = new SaveFormRulesRequest(request.ExpectedRevision,request.Rules?.Select(rule => rule is null ? null : new FormRuleInput(rule.Id,rule.Event,
            rule.Conditions?.Select(condition => condition is null ? null : new FormRecordCondition(condition.FieldId,condition.Operator!,condition.Values)).ToArray(),
            rule.Actions?.Select(action => action is null ? null : new FormRuleActionInput(action.Id,action.Kind,action.RoleId,action.Text,action.IncludePdf)).ToArray())).ToArray());
        return (await service.SaveAsync(id,mapped,cancellationToken)).ToCreatedResult(this,nameof(GetRevision),value => new { id = value.DefinitionId, revision = value.Revision });
    }
}
