using ArquitecturaBase.Api.Authorization;
using ArquitecturaBase.Api.ErrorHandling;
using ArquitecturaBase.Api.Modules.Arca.Contracts.Invoicing;
using ArquitecturaBase.Api.OpenApi;
using ArquitecturaBase.Application.Modules.Arca.Interfaces.Services.Invoicing;
using ArquitecturaBase.Application.Modules.Arca.Models.Invoicing;
using ArquitecturaBase.Domain.Modules.Arca;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace ArquitecturaBase.Api.Modules.Arca.Controllers;

/// <summary>
/// El punto de venta de la plataforma y los datos del comprobante: consultar a ARCA, elegir y guardar piden también
/// <c>arca.manage</c>.
/// </summary>
[ApiController]
[Route("api/arca/invoicing")]
[Tags("ARCA")]
[HasPermission(ArcaPermissions.Read)]
[EnableRateLimiting(ArcaApiRegistration.ReadPolicy)]
[RequestSizeLimit(4096)]
public sealed class ArcaInvoicingController(IArcaInvoicingSetupService setup) : ControllerBase
{
    [HttpGet("settings")]
    [ProducesResponseType<ArcaInvoicingSettingsResponse>(StatusCodes.Status200OK)]
    [ProducesProblem(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetSettings(CancellationToken cancellationToken) => (await setup.GetAsync(cancellationToken)).ToActionResult(this);

    [HttpGet("points-of-sale")]
    [HasPermission(ArcaPermissions.Manage)]
    [EnableRateLimiting(ArcaApiRegistration.RemotePolicy)]
    [ProducesResponseType<IReadOnlyList<ArcaPointOfSaleResponse>>(StatusCodes.Status200OK)]
    [ProducesProblem(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> ListPointsOfSale(CancellationToken cancellationToken) =>
        (await setup.ListPointsOfSaleAsync(cancellationToken)).ToActionResult(this);

    [HttpPut("point-of-sale")]
    [HasPermission(ArcaPermissions.Manage)]
    [EnableRateLimiting(ArcaApiRegistration.RemotePolicy)]
    [ProducesResponseType<ArcaInvoicingSettingsResponse>(StatusCodes.Status200OK)]
    [ProducesProblem(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> ChoosePointOfSale([FromBody] ChooseArcaPointOfSaleHttpRequest request, CancellationToken cancellationToken) =>
        (await setup.ChoosePointOfSaleAsync(request.PointOfSale, cancellationToken)).ToActionResult(this);

    [HttpPut("voucher-data")]
    [HasPermission(ArcaPermissions.Manage)]
    [EnableRateLimiting(ArcaApiRegistration.WritePolicy)]
    [ProducesResponseType<ArcaInvoicingSettingsResponse>(StatusCodes.Status200OK)]
    [ProducesProblem(StatusCodes.Status400BadRequest)]
    [ProducesProblem(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> SetVoucherData([FromBody] SetArcaVoucherDataHttpRequest request, CancellationToken cancellationToken) =>
        (await setup.SetVoucherDataAsync(request.GrossIncomeNumber, request.ActivityStartOn, cancellationToken)).ToActionResult(this);
}
