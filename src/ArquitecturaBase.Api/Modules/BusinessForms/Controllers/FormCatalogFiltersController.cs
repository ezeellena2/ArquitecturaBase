using ArquitecturaBase.Api.Authorization;
using ArquitecturaBase.Api.ErrorHandling;
using ArquitecturaBase.Api.Modules.BusinessForms.Contracts;
using ArquitecturaBase.Api.OpenApi;
using ArquitecturaBase.Application.Modules.BusinessForms.Interfaces.Services;
using ArquitecturaBase.Application.Modules.BusinessForms.Models;
using ArquitecturaBase.Domain.Modules.BusinessForms;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace ArquitecturaBase.Api.Modules.BusinessForms.Controllers;

[ApiController]
[Route("api/forms")]
[Tags("Forms")]
[HasPermission(BusinessFormsPermissions.CatalogsRead)]
[RequestSizeLimit(524288)]
public sealed class FormCatalogFiltersController(IFormCatalogFiltersService service) : ControllerBase
{
    [HttpGet("catalog-filter-options")]
    [EnableRateLimiting(BusinessFormsApiRegistration.ReadPolicy)]
    [ProducesResponseType<FormCatalogFilterOptionsResponse>(StatusCodes.Status200OK)]
    public async Task<IActionResult> Options([FromQuery] FormCatalogFilterOptionsQuery query, CancellationToken cancellationToken) =>
        (await service.GetOptionsAsync(new FormCatalogFilterOptionsRequest
        {
            Page = query.Page,
            PageSize = query.PageSize,
            Sort = query.Sort,
            Search = query.Search,
        }, cancellationToken)).ToActionResult(this);

    [HttpPost("catalog-previews")]
    [HasPermission(BusinessFormsPermissions.CatalogsManage)]
    [EnableRateLimiting(BusinessFormsApiRegistration.WritePolicy)]
    [ProducesResponseType<FormCatalogFilterPreviewResponse>(StatusCodes.Status200OK)]
    [ProducesProblem(StatusCodes.Status400BadRequest)]
    [ProducesProblem(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Preview([FromBody] FormCatalogFilterPreviewHttpRequest request, CancellationToken cancellationToken) =>
        (await service.PreviewAsync(new FormCatalogFilterPreviewRequest(request.SampleUserId, Map(request.Filters)), cancellationToken))
        .ToActionResult(this);

    private static FormCatalogFilterDefinition? Map(FormCatalogFilterDefinitionHttpRequest? filters) => filters is null ? null
        : new(filters.TextColumn, filters.DetailColumn,
            filters.Conditions?.Select(condition => condition is null ? null
                : new FormCatalogFilterCondition(condition.Column, condition.Operator, condition.Value)).ToArray(),
            filters.ExcludeRecipient, filters.SortColumn, filters.SortDescending);

}
