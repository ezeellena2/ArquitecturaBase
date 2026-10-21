using ArquitecturaBase.Api.Authorization;
using ArquitecturaBase.Api.ErrorHandling;
using ArquitecturaBase.Api.Modules.BusinessForms.Contracts;
using ArquitecturaBase.Api.OpenApi;
using ArquitecturaBase.Application.Modules.BusinessForms.Interfaces.Services;
using ArquitecturaBase.Domain.Modules.BusinessForms;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace ArquitecturaBase.Api.Modules.BusinessForms.Controllers;

[ApiController]
[Route("api/forms/definitions/{id:guid}")]
[Tags("Forms")]
[HasPermission(BusinessFormsPermissions.Read)]
[HasPermission(BusinessFormsPermissions.Manage)]
[EnableRateLimiting(BusinessFormsApiRegistration.WritePolicy)]
[RequestSizeLimit(524288)]
public sealed class FormDeletionsController(IFormLifecycleService service) : ControllerBase
{
    [HttpDelete]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesProblem(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Delete([FromRoute] Guid id, [FromBody] DeleteFormHttpRequest request,
        CancellationToken cancellationToken)
    {
        Response.Headers.CacheControl = "no-store";
        return (await service.DeleteAsync(id, new(request.ExpectedRevision), cancellationToken)).ToActionResult(this);
    }
}