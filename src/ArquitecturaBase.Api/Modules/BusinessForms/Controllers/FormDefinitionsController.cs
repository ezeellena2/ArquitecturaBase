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
    [ProducesResponseType<FormDefinitionDetailHttpResponse>(StatusCodes.Status200OK)]
    [ProducesProblem(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Get([FromRoute] Guid id, CancellationToken cancellationToken)
    {
        var result = await service.GetAsync(id, cancellationToken);
        if (result.IsFailure) return result.ToActionResult(this);
        var detail = result.Value;
        var operation = detail.Publication.CurrentOperation;
        var current = operation is null ? null : new FormCurrentPublicationHttpResponse(operation.VersionId, operation.VersionNumber,
            operation.DraftRevision, operation.State, operation.Stage, operation.RequiresReview, operation.CanRetry, operation.RequestId,
            operation.RequestId is { } requestId ? PublicationRecoveryPath(id, requestId) : null);
        var publication = new FormPublicationOverviewHttpResponse(detail.Publication.ActiveVersion, current, detail.Publication.DisplayState);
        return ArquitecturaBase.Domain.Results.Result.Success(new FormDefinitionDetailHttpResponse(detail.Definition, detail.Capabilities, detail.Audit, publication)).ToActionResult(this);
    }
    [HttpPost]
    [HasPermission(BusinessFormsPermissions.Manage)]
    [EnableRateLimiting(BusinessFormsApiRegistration.WritePolicy)]
    [ProducesResponseType<FormDraftCommitHttpResponse>(StatusCodes.Status201Created)]
    [ProducesProblem(StatusCodes.Status409Conflict)]
    [ProducesProblem(StatusCodes.Status410Gone)]
    public async Task<IActionResult> Create([FromBody] SaveFormDraftHttpRequest request, CancellationToken cancellationToken)
    {
        var result = await service.CreateAsync(Map(request), cancellationToken);
        if (result.IsFailure) return result.ToCreatedResult(this, nameof(Get), value => new { id = value.Definition.Id });
        return ArquitecturaBase.Domain.Results.Result.Success(MapCommit(result.Value)).ToCreatedResult(this, nameof(Get), value => new { id = value.Definition.Id });
    }
    [HttpPut("{id:guid}/draft")]
    [HasPermission(BusinessFormsPermissions.Manage)]
    [EnableRateLimiting(BusinessFormsApiRegistration.WritePolicy)]
    [ProducesResponseType<FormDraftCommitHttpResponse>(StatusCodes.Status200OK)]
    [ProducesProblem(StatusCodes.Status404NotFound)]
    [ProducesProblem(StatusCodes.Status409Conflict)]
    [ProducesProblem(StatusCodes.Status410Gone)]
    public async Task<IActionResult> Update([FromRoute] Guid id, [FromBody] SaveFormDraftHttpRequest request, CancellationToken cancellationToken)
    {
        var result = await service.UpdateAsync(id, Map(request), cancellationToken);
        return result.IsFailure ? result.ToActionResult(this) : ArquitecturaBase.Domain.Results.Result.Success(MapCommit(result.Value)).ToActionResult(this);
    }
    private FormDraftCommitHttpResponse MapCommit(FormDraftCommitResponse response) => new(response.RequestId, response.Definition,
        response.IdentityMap, new(Url.Action(nameof(FormDraftChangesController.Get), "FormDraftChanges", new { requestId = response.RequestId })
            ?? throw new InvalidOperationException("The draft change route could not be generated."), response.Recovery.ResultExpiresAtUtc));
    private string PublicationRecoveryPath(Guid id, Guid requestId) => Url.Action(nameof(FormPublicationsController.Get), "FormPublications",
        new { id, requestId }) ?? throw new InvalidOperationException("The publication status route could not be generated.");
    private static ListFormDefinitionsRequest Map(ListFormDefinitionsQuery query) => new()
        { Page = query.Page, PageSize = query.PageSize, Search = query.Search, Sort = query.Sort, PublicationFilter = query.PublicationFilter };
    private static SaveFormDraftRequest Map(SaveFormDraftHttpRequest request) => new(request.RequestId, request.Name, request.Prefix,
        request.Draft is { } draft ? new(draft.Format, draft.Culture, draft.SubmitLabel,
            draft.Fields?.Select(field => field is null ? null! : new FormFieldInput(field.LocalId, field.FieldId, field.ReplacesFieldId,
                field.Type, field.Label, field.Help, field.Required, field.MaxLength,
                field.Options?.Select(option => option is null ? null! : new FormChoiceInput(option.LocalId, option.ChoiceId, option.Label)).ToArray())).ToArray()!) : null!, request.ExpectedRevision);
}
