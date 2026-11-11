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

/// <summary>«Completar con ARCA» (CA1): which registry completes a catalog, and what it knows of a tax id. Nothing is saved.</summary>
[ApiController]
[Route("api/forms/catalogs/{id:guid}/lookup")]
[Tags("Forms")]
[HasPermission(BusinessFormsPermissions.CatalogsManage)]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public sealed class FormCatalogLookupsController(IFormCatalogLookupService service) : ControllerBase
{
    [HttpGet]
    [EnableRateLimiting(BusinessFormsApiRegistration.ReadPolicy)]
    [ProducesResponseType<FormCatalogLookupResponse>(StatusCodes.Status200OK)]
    [ProducesProblem(StatusCodes.Status403Forbidden)]
    [ProducesProblem(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Get([FromRoute] Guid id, CancellationToken cancellationToken) =>
        (await service.GetAsync(id, cancellationToken)).ToActionResult(this);

    [HttpPost]
    [EnableRateLimiting(BusinessFormsApiRegistration.WritePolicy)]
    [RequestSizeLimit(8192)]
    [ProducesResponseType<FormCatalogLookupResultResponse>(StatusCodes.Status200OK)]
    [ProducesProblem(StatusCodes.Status400BadRequest)]
    [ProducesProblem(StatusCodes.Status403Forbidden)]
    [ProducesProblem(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Lookup([FromRoute] Guid id, [FromBody] LookupFormCatalogPartyHttpRequest request, CancellationToken cancellationToken) =>
        (await service.LookupAsync(id, new LookupFormCatalogPartyRequest(request.TaxId), cancellationToken)).ToActionResult(this);
}
