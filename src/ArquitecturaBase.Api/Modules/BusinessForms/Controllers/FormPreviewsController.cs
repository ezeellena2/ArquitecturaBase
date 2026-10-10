using ArquitecturaBase.Api.Authorization;
using ArquitecturaBase.Api.ErrorHandling;
using ArquitecturaBase.Api.OpenApi;
using ArquitecturaBase.Api.Modules.BusinessForms.Contracts;
using ArquitecturaBase.Application.Modules.BusinessForms.Interfaces.Services;
using ArquitecturaBase.Application.Models.StructuredData;
using ArquitecturaBase.Domain.Modules.BusinessForms;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
namespace ArquitecturaBase.Api.Modules.BusinessForms.Controllers;
[ApiController]
[Route("api/forms/definitions/{id:guid}/versions/{versionId:guid}/preview")]
[Tags("Forms")]
[HasPermission(BusinessFormsPermissions.Read)]
public sealed class FormPreviewsController(IFormPreviewService service) : ControllerBase
{
    [HttpGet]
    [EnableRateLimiting(BusinessFormsApiRegistration.ReadPolicy)]
    [ProducesResponseType<StructuredFormPreviewResponse>(StatusCodes.Status200OK)]
    [ProducesProblem(StatusCodes.Status404NotFound)]
    [ProducesProblem(StatusCodes.Status400BadRequest)]
    [ProducesProblem(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Get([FromRoute] Guid id, [FromRoute] Guid versionId, [FromQuery] FormPreviewQuery query, CancellationToken cancellationToken)
    {
        Response.Headers.CacheControl = "no-store";
        return (await service.GetAsync(id, versionId, query.Culture, cancellationToken)).ToActionResult(this);
    }
}
