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

/// <summary>
/// Correcting a response in the app (CW10-A): whoever loaded it, with «Completar formularios», or anyone with «Corregir y anular
/// respuestas» and «Ver respuestas». The service decides which response each one may correct.
/// </summary>
[ApiController]
[Route("api/forms/entries/{definitionId:guid}/records/{recordId:guid}")]
[Tags("Form entries")]
[HasAnyPermission(BusinessFormsPermissions.RecordsCreate, BusinessFormsPermissions.RecordsAmend)]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public sealed class FormEntryCorrectionsController(IFormEntryService service) : ControllerBase
{
    // Up to ten files of a response, 100 MiB in total, plus the answers and the multipart envelope.
    private const long MaximumRequestBytes = 101L * 1024 * 1024;

    [HttpGet]
    [EnableRateLimiting(BusinessFormsApiRegistration.ReadPolicy)]
    [ProducesResponseType<FormEntryCorrectionResponse>(StatusCodes.Status200OK)]
    [ProducesProblem(StatusCodes.Status403Forbidden)]
    [ProducesProblem(StatusCodes.Status404NotFound)]
    [ProducesProblem(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Get([FromRoute] Guid definitionId, [FromRoute] Guid recordId, CancellationToken cancellationToken) =>
        (await service.GetCorrectionAsync(definitionId, recordId, cancellationToken)).ToActionResult(this);

    [HttpGet("versions/{versionId:guid}/questions/{fieldId:guid}/options")]
    [EnableRateLimiting(BusinessFormsApiRegistration.ReadPolicy)]
    [ProducesResponseType<PagedResult<FormEntryOptionResponse>>(StatusCodes.Status200OK)]
    [ProducesProblem(StatusCodes.Status400BadRequest)]
    [ProducesProblem(StatusCodes.Status403Forbidden)]
    [ProducesProblem(StatusCodes.Status404NotFound)]
    [ProducesProblem(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> ListOptions([FromRoute] Guid definitionId, [FromRoute] Guid recordId, [FromRoute] Guid versionId,
        [FromRoute] Guid fieldId, [FromQuery] FormEntryOptionsQuery query, CancellationToken cancellationToken) =>
        (await service.ListOptionsAsync(definitionId, versionId, fieldId,
            new() { Search = query.Search, Page = query.Page, PageSize = query.PageSize, Answers = query.Answers, RecordId = recordId },
            cancellationToken)).ToActionResult(this);

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
    public async Task<IActionResult> Correct([FromRoute] Guid definitionId, [FromRoute] Guid recordId, [FromForm] CorrectFormEntryHttpRequest request,
        CancellationToken cancellationToken) =>
        (await service.CorrectAsync(definitionId, recordId, await FormEntryRequestMapping.MapAsync(request, cancellationToken), cancellationToken))
            .ToActionResult(this);
}
