using ArquitecturaBase.Api.Authorization;
using ArquitecturaBase.Api.ErrorHandling;
using ArquitecturaBase.Api.OpenApi;
using ArquitecturaBase.Application.Modules.Arca.Interfaces.Services.Registry;
using ArquitecturaBase.Application.Modules.Arca.Models.Registry;
using ArquitecturaBase.Domain.Modules.Arca;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace ArquitecturaBase.Api.Modules.Arca.Controllers;

/// <summary>Los datos de la empresa que trajo el padrón en la última prueba.</summary>
[ApiController]
[Route("api/arca/registry")]
[Tags("ARCA")]
[HasPermission(ArcaPermissions.Read)]
[EnableRateLimiting(ArcaApiRegistration.ReadPolicy)]
public sealed class ArcaRegistryController(IArcaRegistryCompanyService companies) : ControllerBase
{
    [HttpGet("company")]
    [ProducesResponseType<ArcaCompanyResponse>(StatusCodes.Status200OK)]
    [ProducesProblem(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetCompany(CancellationToken cancellationToken) => (await companies.GetAsync(cancellationToken)).ToActionResult(this);
}
