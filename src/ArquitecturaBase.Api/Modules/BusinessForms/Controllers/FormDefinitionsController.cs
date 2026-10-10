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
[Route("api/forms/definitions")]
[Tags("Forms")]
[HasPermission(BusinessFormsPermissions.Read)]
[EnableRateLimiting(BusinessFormsApiRegistration.ReadPolicy)]
[RequestSizeLimit(524288)]
public sealed class FormDefinitionsController(IFormDraftService service) : ControllerBase
{
    [HttpGet]
    [ProducesResponseType<PagedResult<FormDefinitionListResponse>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> List([FromQuery] ListFormDefinitionsQuery query, CancellationToken cancellationToken) =>
        (await service.ListAsync(Map(query), cancellationToken)).ToActionResult(this);
    [HttpGet("facets")]
    [ProducesResponseType<FormDefinitionFacetsResponse>(StatusCodes.Status200OK)]
    public async Task<IActionResult> Facets([FromQuery] ListFormDefinitionsQuery query, CancellationToken cancellationToken) =>
        (await service.GetFacetsAsync(Map(query), cancellationToken)).ToActionResult(this);
    [HttpGet("{id:guid}")]
    [ProducesResponseType<FormDefinitionDetailResponse>(StatusCodes.Status200OK)]
    [ProducesProblem(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Get([FromRoute] Guid id, CancellationToken cancellationToken) => (await service.GetAsync(id, cancellationToken)).ToActionResult(this);
    [HttpPost]
    [HasPermission(BusinessFormsPermissions.Manage)]
    [EnableRateLimiting(BusinessFormsApiRegistration.WritePolicy)]
    [ProducesResponseType<FormDraftCommitResponse>(StatusCodes.Status201Created)]
    [ProducesProblem(StatusCodes.Status409Conflict)]
    [ProducesProblem(StatusCodes.Status410Gone)]
    public async Task<IActionResult> Create([FromBody] SaveFormDraftHttpRequest request, CancellationToken cancellationToken) =>
        (await service.CreateAsync(Map(request), cancellationToken)).ToCreatedResult(this, nameof(Get), result => new { id = result.Definition.Id });
    [HttpPut("{id:guid}/draft")]
    [HasPermission(BusinessFormsPermissions.Manage)]
    [EnableRateLimiting(BusinessFormsApiRegistration.WritePolicy)]
    [ProducesResponseType<FormDraftCommitResponse>(StatusCodes.Status200OK)]
    [ProducesProblem(StatusCodes.Status404NotFound)]
    [ProducesProblem(StatusCodes.Status409Conflict)]
    [ProducesProblem(StatusCodes.Status410Gone)]
    public async Task<IActionResult> Update([FromRoute] Guid id, [FromBody] SaveFormDraftHttpRequest request, CancellationToken cancellationToken) =>
        (await service.UpdateAsync(id, Map(request), cancellationToken)).ToActionResult(this);
    private static ListFormDefinitionsRequest Map(ListFormDefinitionsQuery query) => new()
        { Page = query.Page, PageSize = query.PageSize, Search = query.Search, Sort = query.Sort, PublicationFilter = query.PublicationFilter };
    private static SaveFormDraftRequest Map(SaveFormDraftHttpRequest request) => new(request.RequestId, request.Name, request.Prefix,
        request.Draft is { } draft ? new(draft.Format, draft.Culture, draft.SubmitLabel,
            draft.Fields?.Select(field => field is null ? null! : new FormFieldInput(field.LocalId, field.FieldId, field.ReplacesFieldId,
                field.Type, field.Label, field.Help, field.Required, field.MaxLength,
                field.Options?.Select(option => option is null ? null! : new FormChoiceInput(option.LocalId, option.ChoiceId, option.Label)).ToArray())).ToArray()!) : null!, request.ExpectedRevision);
}
