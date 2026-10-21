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
[Route("api/forms/definitions/{id:guid}/deactivation")]
[Tags("Forms")]
[HasPermission(BusinessFormsPermissions.Read)]
[HasPermission(BusinessFormsPermissions.Publish)]
[EnableRateLimiting(BusinessFormsApiRegistration.ReadPolicy)]
[RequestSizeLimit(524288)]
public sealed class FormDeactivationsController(IFormLifecycleService service) : ControllerBase
{
    [HttpGet]
    [ProducesResponseType<FormDeactivationPreviewResponse>(StatusCodes.Status200OK)]
    public async Task<IActionResult> Get([FromRoute] Guid id, CancellationToken cancellationToken)
    {
        Response.Headers.CacheControl = "no-store";
        return (await service.GetDeactivationAsync(id, cancellationToken)).ToActionResult(this);
    }

    [HttpPost]
    [EnableRateLimiting(BusinessFormsApiRegistration.WritePolicy)]
    [ProducesResponseType<FormDeactivationResponse>(StatusCodes.Status200OK)]
    [ProducesProblem(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Deactivate([FromRoute] Guid id, [FromBody] DeactivateFormHttpRequest request,
        CancellationToken cancellationToken)
    {
        Response.Headers.CacheControl = "no-store";
        return (await service.DeactivateAsync(id, new(request.ExpectedRevision), cancellationToken)).ToActionResult(this);
    }
}
