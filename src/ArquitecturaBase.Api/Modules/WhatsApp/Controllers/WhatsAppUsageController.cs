using ArquitecturaBase.Api.Authorization;
using ArquitecturaBase.Api.ErrorHandling;
using ArquitecturaBase.Api.Modules.WhatsApp.Contracts;
using ArquitecturaBase.Application.Modules.WhatsApp.Interfaces.Services;
using ArquitecturaBase.Application.Modules.WhatsApp.Models;
using ArquitecturaBase.Domain.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace ArquitecturaBase.Api.Modules.WhatsApp.Controllers;

/// <summary>
/// Configuración › Consumo, the WhatsApp zone (T7, PR14 to PR16): what Meta reports for the installation's number in a business month. It asks
/// for «Configuración» like Configuración › WhatsApp, and the route exists with the transport on or off: without it the answer says WhatsApp is
/// not configured.
/// </summary>
[ApiController]
[Route("api/whatsapp/usage")]
[Tags("WhatsApp usage")]
[HasPermission(Permissions.Settings.Manage)]
[EnableRateLimiting(WhatsAppApiRegistration.UsageRateLimitPolicy)]
public sealed class WhatsAppUsageController(IWhatsAppUsageService service) : ControllerBase
{
    [HttpGet]
    [ProducesResponseType<WhatsAppUsageResponse>(StatusCodes.Status200OK)]
    public async Task<IActionResult> Get([FromQuery] WhatsAppUsageQuery query, CancellationToken cancellationToken) =>
        (await service.GetAsync(new(query.Month), cancellationToken)).ToActionResult(this);
}
