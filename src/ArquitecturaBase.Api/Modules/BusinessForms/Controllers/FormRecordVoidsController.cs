using ArquitecturaBase.Api.Authorization;
using ArquitecturaBase.Api.ErrorHandling;
using ArquitecturaBase.Api.Modules.BusinessForms.Contracts;
using ArquitecturaBase.Api.OpenApi;
using ArquitecturaBase.Application.Modules.BusinessForms.Interfaces.Services;
using ArquitecturaBase.Domain.Modules.BusinessForms;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace ArquitecturaBase.Api.Modules.BusinessForms.Controllers;

/// <summary>
/// Voiding a response (CW11-A): whoever loaded it, with «Completar formularios», or anyone with «Corregir y anular respuestas» and
/// «Ver respuestas». The service decides which response each one may void; it answers 204 after the commit.
/// </summary>
[ApiController]
[Route("api/forms/definitions/{definitionId:guid}/records/{recordId:guid}/void")]
[Tags("Form responses")]
[HasAnyPermission(BusinessFormsPermissions.RecordsCreate, BusinessFormsPermissions.RecordsAmend)]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public sealed class FormRecordVoidsController(IFormRecordVoidService service) : ControllerBase
{
    [HttpPost]
    [EnableRateLimiting(BusinessFormsApiRegistration.WritePolicy)]
    [RequestSizeLimit(524288)]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesProblem(StatusCodes.Status400BadRequest)]
    [ProducesProblem(StatusCodes.Status403Forbidden)]
    [ProducesProblem(StatusCodes.Status404NotFound)]
    [ProducesProblem(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Void([FromRoute] Guid definitionId, [FromRoute] Guid recordId, [FromBody] VoidFormRecordHttpRequest request,
        CancellationToken cancellationToken) =>
        (await service.VoidAsync(definitionId, recordId, new(request.Reason), cancellationToken)).ToActionResult(this);
}
