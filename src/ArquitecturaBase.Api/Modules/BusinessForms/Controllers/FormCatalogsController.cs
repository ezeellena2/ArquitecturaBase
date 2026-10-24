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
[Route("api/forms/catalogs")]
[Tags("Forms")]
[HasPermission(BusinessFormsPermissions.CatalogsRead)]
[EnableRateLimiting(BusinessFormsApiRegistration.ReadPolicy)]
[RequestSizeLimit(524288)]
public sealed class FormCatalogsController(IFormCatalogService service, IFormCatalogFiltersService filtersService,
    IFormCatalogProposalService proposals) : ControllerBase
{
    [HttpGet]
    [ProducesResponseType<FormCatalogsPageResponse>(StatusCodes.Status200OK)]
    public async Task<IActionResult> List([FromQuery] FormCatalogsQuery query, CancellationToken cancellationToken) =>
        (await service.ListAsync(Map(query), cancellationToken)).ToActionResult(this);

    [HttpGet("{id:guid}")]
    [ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
    [ProducesResponseType<FormCatalogDetailResponse>(StatusCodes.Status200OK)]
    [ProducesProblem(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Get([FromRoute] Guid id, CancellationToken cancellationToken) =>
        (await service.GetAsync(id, cancellationToken)).ToActionResult(this);

    [HttpGet("{id:guid}/items")]
    [ProducesResponseType<PagedResult<FormCatalogItemResponse>>(StatusCodes.Status200OK)]
    [ProducesProblem(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> ListItems([FromRoute] Guid id, [FromQuery] FormCatalogItemsQuery query, CancellationToken cancellationToken) =>
        (await service.ListItemsAsync(id, Map(query), cancellationToken)).ToActionResult(this);

    [HttpPost("{id:guid}/presentation-checks")]
    [ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
    [ProducesResponseType<FormCatalogPresentationCheckResponse>(StatusCodes.Status200OK)]
    [ProducesProblem(StatusCodes.Status400BadRequest)]
    [ProducesProblem(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> CheckPresentation([FromRoute] Guid id, [FromBody] FormCatalogPresentationCheckHttpRequest request,
        CancellationToken cancellationToken) =>
        (await service.CheckPresentationAsync(id, new(Map(request.Presentation)), cancellationToken)).ToActionResult(this);

    [HttpPost]
    [HasPermission(BusinessFormsPermissions.CatalogsManage)]
    [EnableRateLimiting(BusinessFormsApiRegistration.WritePolicy)]
    [ProducesResponseType<FormCatalogSaveResponse>(StatusCodes.Status201Created)]
    [ProducesProblem(StatusCodes.Status400BadRequest)]
    [ProducesProblem(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Create([FromBody] CreateManualCatalogHttpRequest request, CancellationToken cancellationToken)
    {
        var result = await service.CreateManualAsync(Map(request), cancellationToken);
        return result.ToCreatedResult(this, nameof(Get), value => new { id = value.Catalog.Id });
    }

    [HttpPost("filters")]
    [HasPermission(BusinessFormsPermissions.CatalogsManage)]
    [EnableRateLimiting(BusinessFormsApiRegistration.WritePolicy)]
    [ProducesResponseType<FormCatalogSaveResponse>(StatusCodes.Status201Created)]
    [ProducesProblem(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> CreateFilters([FromBody] CreateFiltersCatalogHttpRequest request, CancellationToken cancellationToken)
    {
        var result = await filtersService.CreateAsync(new CreateFiltersCatalogRequest(
            request.Name, request.Description, Map(request.Filters)), cancellationToken);
        return result.ToCreatedResult(this, nameof(Get), value => new { id = value.Catalog.Id });
    }

    [HttpPut("{id:guid}/filters")]
    [HasPermission(BusinessFormsPermissions.CatalogsManage)]
    [EnableRateLimiting(BusinessFormsApiRegistration.WritePolicy)]
    [ProducesResponseType<FormCatalogSaveResponse>(StatusCodes.Status200OK)]
    [ProducesProblem(StatusCodes.Status400BadRequest)]
    [ProducesProblem(StatusCodes.Status404NotFound)]
    [ProducesProblem(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> UpdateFilters([FromRoute] Guid id, [FromBody] UpdateFiltersCatalogHttpRequest request,
        CancellationToken cancellationToken) =>
        (await filtersService.UpdateAsync(id, new UpdateFiltersCatalogRequest(request.ExpectedRevision,
            request.Name, request.Description, Map(request.Filters), request.IsActive), cancellationToken)).ToActionResult(this);

    [HttpPut("{id:guid}")]
    [HasPermission(BusinessFormsPermissions.CatalogsManage)]
    [EnableRateLimiting(BusinessFormsApiRegistration.WritePolicy)]
    [ProducesResponseType<FormCatalogSaveResponse>(StatusCodes.Status200OK)]
    [ProducesProblem(StatusCodes.Status400BadRequest)]
    [ProducesProblem(StatusCodes.Status404NotFound)]
    [ProducesProblem(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Update([FromRoute] Guid id, [FromBody] UpdateManualCatalogHttpRequest request, CancellationToken cancellationToken) =>
        (await service.UpdateManualAsync(id, Map(request), cancellationToken)).ToActionResult(this);

    [HttpPost("{id:guid}/proposal/approve")]
    [HasPermission(BusinessFormsPermissions.CatalogsManage)]
    [EnableRateLimiting(BusinessFormsApiRegistration.WritePolicy)]
    [ProducesResponseType<FormCatalogSaveResponse>(StatusCodes.Status200OK)]
    [ProducesProblem(StatusCodes.Status400BadRequest)]
    [ProducesProblem(StatusCodes.Status404NotFound)]
    [ProducesProblem(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> ApproveProposal([FromRoute] Guid id, [FromBody] ApproveFormCatalogProposalHttpRequest request,
        CancellationToken cancellationToken) =>
        (await proposals.ApproveAsync(id, new(request.ExpectedRevision, request.Name, request.Description, Map(request.Filters), request.SampleUserId),
            cancellationToken)).ToActionResult(this);

    [HttpPost("{id:guid}/proposal/discard")]
    [HasPermission(BusinessFormsPermissions.CatalogsManage)]
    [EnableRateLimiting(BusinessFormsApiRegistration.WritePolicy)]
    [ProducesResponseType<FormCatalogDetailResponse>(StatusCodes.Status200OK)]
    [ProducesProblem(StatusCodes.Status400BadRequest)]
    [ProducesProblem(StatusCodes.Status404NotFound)]
    [ProducesProblem(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> DiscardProposal([FromRoute] Guid id, [FromBody] DiscardFormCatalogProposalHttpRequest request,
        CancellationToken cancellationToken) =>
        (await proposals.DiscardAsync(id, new(request.ExpectedRevision), cancellationToken)).ToActionResult(this);

    private static CreateManualCatalogRequest Map(CreateManualCatalogHttpRequest request) => new(request.Name, request.Description,
        request.Items?.Select(Map).ToArray(), request.ValueType, request.Fields?.Select(Map).ToArray(), Map(request.Presentation));

    private static ListFormCatalogsRequest Map(FormCatalogsQuery query) => new()
        { Page = query.Page, PageSize = query.PageSize, Sort = query.Sort, Search = query.Search, Kind = query.Kind, State = query.State };

    private static ListFormCatalogItemsRequest Map(FormCatalogItemsQuery query) => new()
        { Page = query.Page, PageSize = query.PageSize, Sort = query.Sort, Search = query.Search, IsActive = query.IsActive, Ids = query.Ids ?? [] };

    private static UpdateManualCatalogRequest Map(UpdateManualCatalogHttpRequest request) => new(request.ExpectedRevision,
        request.Name, request.Description, request.Items?.Select(Map).ToArray(),
        request.Moves?.Select(move => move is null ? null : new FormCatalogManualItemMoveRequest(move.ItemId, move.Direction)).ToArray(), request.IsActive, request.SortAlphabetically, request.Fields?.Select(Map).ToArray(), Map(request.Presentation), request.ConfirmFieldRemovals);

    private static FormCatalogManualItemSaveRequest? Map(FormCatalogManualItemHttpRequest? request) => request is null ? null
        : new(request.LocalId, request.ItemId, request.Code, request.Text, request.Detail, request.IsActive, request.Value, request.Fields);

    private static FormCatalogFieldSaveRequest? Map(FormCatalogFieldHttpRequest? field) => field is null ? null
        : new(field.LocalId, field.Id, field.Key, field.Name, field.Type, field.SourceCatalogId, field.Required,
            field.Unique, field.Min, field.Max, field.MaxLength, field.Role, field.Position, field.IsActive);

    private static FormCatalogPresentation? Map(FormCatalogPresentationHttpRequest? presentation) => presentation is null ? null
        : new(presentation.Title!, presentation.Description!);

    private static FormCatalogFilterDefinition? Map(FormCatalogFilterDefinitionHttpRequest? filters) => filters is null ? null
        : new(filters.TextColumn, filters.DetailColumn,
            filters.Conditions?.Select(condition => condition is null ? null
                : new FormCatalogFilterCondition(condition.Column, condition.Operator, condition.Value)).ToArray(),
            filters.ExcludeRecipient, filters.SortColumn, filters.SortDescending);
}
