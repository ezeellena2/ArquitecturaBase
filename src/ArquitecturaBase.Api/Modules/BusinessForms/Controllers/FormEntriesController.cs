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
[Route("api/forms/entries/{definitionId:guid}")]
[Tags("Form entries")]
[HasPermission(BusinessFormsPermissions.RecordsCreate)]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public sealed class FormEntriesController(IFormEntryService service) : ControllerBase
{
    // Up to ten files of a response, 100 MiB in total, plus the answers and the multipart envelope.
    private const long MaximumRequestBytes = 101L * 1024 * 1024;

    [HttpGet]
    [EnableRateLimiting(BusinessFormsApiRegistration.ReadPolicy)]
    [ProducesResponseType<FormEntryResponse>(StatusCodes.Status200OK)]
    [ProducesProblem(StatusCodes.Status404NotFound)]
    [ProducesProblem(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Get([FromRoute] Guid definitionId, CancellationToken cancellationToken) =>
        (await service.GetAsync(definitionId, cancellationToken)).ToActionResult(this);

    [HttpGet("versions/{versionId:guid}/questions/{fieldId:guid}/options")]
    [EnableRateLimiting(BusinessFormsApiRegistration.ReadPolicy)]
    [ProducesResponseType<PagedResult<FormEntryOptionResponse>>(StatusCodes.Status200OK)]
    [ProducesProblem(StatusCodes.Status400BadRequest)]
    [ProducesProblem(StatusCodes.Status404NotFound)]
    [ProducesProblem(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> ListOptions([FromRoute] Guid definitionId, [FromRoute] Guid versionId, [FromRoute] Guid fieldId,
        [FromQuery] FormEntryOptionsQuery query, CancellationToken cancellationToken) =>
        (await service.ListOptionsAsync(definitionId, versionId, fieldId,
            new() { Search = query.Search, Page = query.Page, PageSize = query.PageSize, Answers = query.Answers }, cancellationToken)).ToActionResult(this);

    [HttpPost]
    [EnableRateLimiting(BusinessFormsApiRegistration.WritePolicy)]
    [Consumes("multipart/form-data")]
    [RequestSizeLimit(MaximumRequestBytes)]
    [RequestFormLimits(MultipartBodyLengthLimit = MaximumRequestBytes)]
    [ProducesResponseType<FormEntrySubmitResponse>(StatusCodes.Status200OK)]
    [ProducesProblem(StatusCodes.Status400BadRequest)]
    [ProducesProblem(StatusCodes.Status403Forbidden)]
    [ProducesProblem(StatusCodes.Status404NotFound)]
    [ProducesProblem(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Submit([FromRoute] Guid definitionId, [FromForm] SubmitFormEntryHttpRequest request, CancellationToken cancellationToken) =>
        (await service.SubmitAsync(definitionId, await FormEntryRequestMapping.MapAsync(request, cancellationToken), cancellationToken)).ToActionResult(this);
}
