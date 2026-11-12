using ArquitecturaBase.Api.Authorization;
using ArquitecturaBase.Api.ErrorHandling;
using ArquitecturaBase.Api.OpenApi;
using ArquitecturaBase.Application.Modules.BusinessForms.Interfaces.Services;
using ArquitecturaBase.Application.Modules.BusinessForms.Models;
using ArquitecturaBase.Domain.Modules.BusinessForms;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace ArquitecturaBase.Api.Modules.BusinessForms.Controllers;

// DOC1, «Cómo viene leyendo»: how the reading of documents of a form did over the last 30 days. The numbers need «Ver formularios»;
// the latest corrections are values of responses and come only with «Ver respuestas», which the service checks again before answering.
[ApiController]
[Route("api/forms/definitions/{id:guid}/document-reading")]
[Tags("Form document reading")]
[HasPermission(BusinessFormsPermissions.Read)]
[EnableRateLimiting(BusinessFormsApiRegistration.ReadPolicy)]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public sealed class FormDocumentReadingController(IFormDocumentReadingStatsService service) : ControllerBase
{
    [HttpGet]
    [ProducesResponseType<FormDocumentReadingStatsResponse>(StatusCodes.Status200OK)]
    [ProducesProblem(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Get([FromRoute] Guid id, CancellationToken cancellationToken) =>
        (await service.GetAsync(id, cancellationToken)).ToActionResult(this);
}
