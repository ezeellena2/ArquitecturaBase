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
[Route("api/forms")]
[Tags("Form responses")]
[HasPermission(BusinessFormsPermissions.RecordsRead)]
[EnableRateLimiting(BusinessFormsApiRegistration.ReadPolicy)]
[RequestSizeLimit(524288)]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public sealed class FormResponsesController(IFormResponseService service) : ControllerBase
{
    [HttpGet("navigation")]
    [ProducesResponseType<PagedResult<FormNavigationResponse>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> Navigation([FromQuery] FormNavigationQuery query, CancellationToken cancellationToken) =>
        (await service.ListNavigationAsync(new() { Page = query.Page, PageSize = query.PageSize, Sort = query.Sort, Search = query.Search }, cancellationToken)).ToActionResult(this);
    [HttpGet("definitions/{id:guid}/records")]
    [ProducesResponseType<FormResponseSheetResponse>(StatusCodes.Status200OK)]
    [ProducesProblem(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> List([FromRoute] Guid id, [FromQuery] FormRecordsQuery query, CancellationToken cancellationToken) =>
        (await service.ListAsync(id, new() { Page = query.Page, PageSize = query.PageSize, Sort = query.Sort, SortFieldId = query.SortFieldId, VersionIds = query.VersionIds ?? [], State = query.State }, cancellationToken)).ToActionResult(this);
    [HttpPost("definitions/{id:guid}/records/search")]
    [ProducesResponseType<FormResponseSheetResponse>(StatusCodes.Status200OK)]
    [ProducesProblem(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Search([FromRoute] Guid id, [FromBody] FormRecordsSearchHttpRequest request, CancellationToken cancellationToken) =>
        (await service.ListAsync(id, FormRecordsRequestMapping.Map(request), cancellationToken)).ToActionResult(this);
    [HttpPost("definitions/{definitionId:guid}/records/{recordId:guid}/navigation")]
    [ProducesResponseType<FormRecordNavigationResponse>(StatusCodes.Status200OK)]
    [ProducesProblem(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> RecordNavigation([FromRoute] Guid definitionId, [FromRoute] Guid recordId,
        [FromBody] FormRecordsSearchHttpRequest request, CancellationToken cancellationToken) =>
        (await service.FindRecordNavigationAsync(definitionId, recordId, FormRecordsRequestMapping.Map(request), cancellationToken)).ToActionResult(this);
    [HttpPost("definitions/{id:guid}/records/filter-counts/search")]
    [ProducesResponseType<PagedResult<FormResponseFacetOptionResponse>>(StatusCodes.Status200OK)]
    [ProducesProblem(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Counts([FromRoute] Guid id, [FromBody] FormRecordFacetHttpRequest request, CancellationToken cancellationToken) =>
        (await service.CountFacetsAsync(id, new() { Filter = FormRecordsRequestMapping.Map(request.Filter), Dimension = request.Dimension, FieldId = request.FieldId,
            Page = request.Page, PageSize = request.PageSize, Sort = request.Sort, Search = request.Search }, cancellationToken)).ToActionResult(this);
    [HttpGet("records/{id:guid}")]
    [ProducesResponseType<FormResponseDetailResponse>(StatusCodes.Status200OK)]
    [ProducesProblem(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Get([FromRoute] Guid id, CancellationToken cancellationToken) =>
        (await service.GetAsync(id, cancellationToken)).ToActionResult(this);
}
