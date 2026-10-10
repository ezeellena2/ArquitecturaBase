using ArquitecturaBase.Api.Authorization;
using ArquitecturaBase.Api.ErrorHandling;
using ArquitecturaBase.Api.OpenApi;
using ArquitecturaBase.Application.Modules.BusinessForms.Interfaces.Services;
using ArquitecturaBase.Application.Modules.BusinessForms.Models;
using ArquitecturaBase.Domain.Modules.BusinessForms;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
namespace ArquitecturaBase.Api.Modules.BusinessForms.Controllers;
[ApiController]
[Route("api/forms/draft-changes")]
[Tags("Forms")]
[HasPermission(BusinessFormsPermissions.Read)]
[EnableRateLimiting(BusinessFormsApiRegistration.ReadPolicy)]
public sealed class FormDraftChangesController(IFormDraftService service) : ControllerBase
{
    [HttpGet("{requestId:guid}")]
    [ProducesResponseType<FormRecoveredChangeResponse>(StatusCodes.Status200OK)]
    [ProducesProblem(StatusCodes.Status404NotFound)]
    [ProducesProblem(StatusCodes.Status410Gone)]
    public async Task<IActionResult> Get([FromRoute] Guid requestId, CancellationToken cancellationToken) =>
        (await service.GetChangeAsync(requestId, cancellationToken)).ToActionResult(this);
}
