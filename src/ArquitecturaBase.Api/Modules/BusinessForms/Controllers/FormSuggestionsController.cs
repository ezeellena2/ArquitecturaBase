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
[Route("api/forms/definitions/{id:guid}/suggestions")]
[Tags("Form suggestions")]
[HasPermission(BusinessFormsPermissions.SuggestionsManage)]
[EnableRateLimiting(BusinessFormsApiRegistration.ReadPolicy)]
[RequestSizeLimit(524288)]
public sealed class FormSuggestionsController(IFormSuggestionService service) : ControllerBase
{
    [HttpGet]
    [ProducesResponseType<FormSuggestionSetResponse>(StatusCodes.Status200OK)]
    [ProducesProblem(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetCurrent([FromRoute] Guid id, CancellationToken cancellationToken)
    {
        Response.Headers.CacheControl = "no-store";
        return (await service.GetCurrentAsync(id,cancellationToken)).ToActionResult(this);
    }
    [HttpGet("revisions")]
    [ProducesResponseType<PagedResult<FormSuggestionRevisionResponse>>(StatusCodes.Status200OK)]
    [ProducesProblem(StatusCodes.Status400BadRequest)]
    [ProducesProblem(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> ListRevisions([FromRoute] Guid id, [FromQuery] FormSuggestionRevisionsQuery query, CancellationToken cancellationToken)
    {
        Response.Headers.CacheControl = "no-store";
        return (await service.ListRevisionsAsync(id,new() { Page = query.Page, PageSize = query.PageSize, Sort = query.Sort },cancellationToken)).ToActionResult(this);
    }
    [HttpGet("revisions/{revision:long}")]
    [ProducesResponseType<FormSuggestionSetResponse>(StatusCodes.Status200OK)]
    [ProducesProblem(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetRevision([FromRoute] Guid id, [FromRoute] long revision, CancellationToken cancellationToken)
    {
        Response.Headers.CacheControl = "no-store";
        return (await service.GetRevisionAsync(id,revision,cancellationToken)).ToActionResult(this);
    }
    [HttpGet("options")]
    [ProducesResponseType<FormSuggestionOptionsResponse>(StatusCodes.Status200OK)]
    [ProducesProblem(StatusCodes.Status404NotFound)]
    [ProducesProblem(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> GetOptions([FromRoute] Guid id, CancellationToken cancellationToken)
    {
        Response.Headers.CacheControl = "no-store";
        return (await service.GetOptionsAsync(id,cancellationToken)).ToActionResult(this);
    }
    [HttpPost]
    [EnableRateLimiting(BusinessFormsApiRegistration.WritePolicy)]
    [ProducesResponseType<FormSuggestionSetResponse>(StatusCodes.Status201Created)]
    [ProducesProblem(StatusCodes.Status400BadRequest)]
    [ProducesProblem(StatusCodes.Status404NotFound)]
    [ProducesProblem(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Save([FromRoute] Guid id, [FromBody] SaveFormSuggestionsHttpRequest request, CancellationToken cancellationToken)
    {
        Response.Headers.CacheControl = "no-store";
        var mapped=new SaveFormSuggestionsRequest(request.ExpectedRevision,request.Suggestions?.Select(suggestion=>suggestion is null?null:new FormSuggestionInput(
            suggestion.Id,suggestion.DestinationDefinitionId,suggestion.Mode,suggestion.When,suggestion.IsActive,
            suggestion.Conditions?.Select(condition=>condition is null?null:new FormRecordCondition(condition.FieldId,condition.Operator,condition.Values)).ToArray(),
            suggestion.Mappings?.Select(mapping=>mapping is null?null:new FormSuggestionMapping(
                mapping.Source is null?null!:new FormSuggestionMappingSource(mapping.Source.Kind,mapping.Source.FieldId),mapping.DestinationFieldId)).ToArray())).ToArray());
        return (await service.SaveAsync(id,mapped,cancellationToken)).ToCreatedResult(this,nameof(GetRevision),value => new { id = value.DefinitionId, revision = value.Revision });
    }
}
