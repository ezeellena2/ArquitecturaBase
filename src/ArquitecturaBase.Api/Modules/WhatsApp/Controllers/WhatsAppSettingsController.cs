using ArquitecturaBase.Api.Authorization;
using ArquitecturaBase.Api.ErrorHandling;
using ArquitecturaBase.Api.Modules.WhatsApp.Contracts;
using ArquitecturaBase.Api.OpenApi;
using ArquitecturaBase.Application.Modules.WhatsApp.Interfaces.Services;
using ArquitecturaBase.Application.Modules.WhatsApp.Models;
using ArquitecturaBase.Domain.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace ArquitecturaBase.Api.Modules.WhatsApp.Controllers;

/// <summary>
/// Configuración › WhatsApp (RC3-A, 2026-11-22): the time to answer a form, the countries that receive codes and the retention of the
/// messages. They are the installation's, so they ask for «Configuración» like General, and the routes exist with the transport on or off:
/// the retention runs anyway.
/// </summary>
[ApiController]
[Route("api/whatsapp/settings")]
[Tags("WhatsApp settings")]
[HasPermission(Permissions.Settings.Manage)]
[EnableRateLimiting(WhatsAppApiRegistration.ChatReadRateLimitPolicy)]
[RequestSizeLimit(16384)]
public sealed class WhatsAppSettingsController(IWhatsAppSettingsService service) : ControllerBase
{
    [HttpGet]
    [ProducesResponseType<WhatsAppSettingsResponse>(StatusCodes.Status200OK)]
    public async Task<IActionResult> Get(CancellationToken cancellationToken) => (await service.GetAsync(cancellationToken)).ToActionResult(this);

    [HttpPut]
    [EnableRateLimiting(WhatsAppApiRegistration.ChatWriteRateLimitPolicy)]
    [ProducesResponseType<WhatsAppSettingsResponse>(StatusCodes.Status200OK)]
    [ProducesProblem(StatusCodes.Status400BadRequest)]
    [ProducesProblem(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Update([FromBody] UpdateWhatsAppSettingsHttpRequest request, CancellationToken cancellationToken) =>
        (await service.UpdateAsync(new(request.ExpectedRevision, request.FlowSessionLifetimeHours, request.AllowedCountries ?? [],
            request.MessageRetentionDays), cancellationToken)).ToActionResult(this);
}
