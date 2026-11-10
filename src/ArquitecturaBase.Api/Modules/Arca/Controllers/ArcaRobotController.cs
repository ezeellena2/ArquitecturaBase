using ArquitecturaBase.Api.Authorization;
using ArquitecturaBase.Api.ErrorHandling;
using ArquitecturaBase.Api.Modules.Arca.Contracts.Connection;
using ArquitecturaBase.Api.OpenApi;
using ArquitecturaBase.Application.Modules.Arca.Interfaces.Services.Connection;
using ArquitecturaBase.Application.Modules.Arca.Models.Connection;
using ArquitecturaBase.Domain.Modules.Arca;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace ArquitecturaBase.Api.Modules.Arca.Controllers;

/// <summary>
/// El robot de delegación (ADR 0026): leer el avance pide <c>arca.read</c>; arrancarlo con la clave fiscal, además
/// <c>arca.manage</c> y el límite de lo que llama a ARCA. La clave no vuelve en ninguna respuesta.
/// </summary>
[ApiController]
[Route("api/arca/connection/robot-runs")]
[Tags("ARCA")]
[HasPermission(ArcaPermissions.Read)]
[EnableRateLimiting(ArcaApiRegistration.ReadPolicy)]
[RequestSizeLimit(16384)]
public sealed class ArcaRobotController(IArcaRobotService service) : ControllerBase
{
    [HttpGet("current")]
    [ProducesResponseType<ArcaRobotRunResponse>(StatusCodes.Status200OK)]
    public async Task<IActionResult> GetCurrent(CancellationToken cancellationToken) => (await service.GetAsync(cancellationToken)).ToActionResult(this);

    [HttpPost]
    [HasPermission(ArcaPermissions.Manage)]
    [EnableRateLimiting(ArcaApiRegistration.RemotePolicy)]
    [ProducesResponseType<ArcaRobotRunResponse>(StatusCodes.Status202Accepted)]
    [ProducesProblem(StatusCodes.Status400BadRequest)]
    [ProducesProblem(StatusCodes.Status404NotFound)]
    [ProducesProblem(StatusCodes.Status409Conflict)]
    [ProducesProblem(StatusCodes.Status429TooManyRequests)]
    public async Task<IActionResult> Start([FromBody] StartArcaRobotRunHttpRequest request, CancellationToken cancellationToken)
    {
        Response.Headers.CacheControl = "no-store";
        var started = await service.StartAsync(new ArcaRobotStartRequest(request.Consent, request.LoginCuit ?? string.Empty,
            new ArcaFiscalKey(request.FiscalKey), request.ExpectedRevision), cancellationToken);
        return started.ToAcceptedResult(this);
    }
}
