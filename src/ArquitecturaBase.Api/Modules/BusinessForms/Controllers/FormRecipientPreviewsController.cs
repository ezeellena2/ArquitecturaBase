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
[Tags("Forms")]
[HasPermission(BusinessFormsPermissions.Read)]
[HasPermission(BusinessFormsPermissions.Manage)]
[EnableRateLimiting(BusinessFormsApiRegistration.ReadPolicy)]
[RequestSizeLimit(524288)]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public sealed class FormRecipientPreviewsController(IFormRecipientPreviewService service) : ControllerBase
{
    [HttpGet("recipient-samples")]
    [ProducesResponseType<PagedResult<FormCatalogFilterSampleUserResponse>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> Samples([FromQuery] FormRecipientSamplesQuery query, CancellationToken ct) =>
        (await service.ListSamplesAsync(new() { Page = query.Page, PageSize = query.PageSize, Search = query.Search }, ct)).ToActionResult(this);

    [HttpPost("definitions/{id:guid}/recipient-previews")]
    [EnableRateLimiting(BusinessFormsApiRegistration.WritePolicy)]
    [ProducesResponseType<FormRecipientPreviewResponse>(StatusCodes.Status200OK)]
    [ProducesProblem(StatusCodes.Status400BadRequest)]
    [ProducesProblem(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Preview([FromRoute] Guid id, [FromBody] FormRecipientPreviewHttpRequest request, CancellationToken ct) =>
        (await service.PreviewAsync(id, new(request.Type, FormDraftRequestMapping.Map(request.Source)!, request.SampleUserId, request.Culture), ct)).ToActionResult(this);
}
