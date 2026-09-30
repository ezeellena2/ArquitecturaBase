using ArquitecturaBase.Api.Authorization;
using ArquitecturaBase.Api.Contracts.Settings;
using ArquitecturaBase.Api.ErrorHandling;
using ArquitecturaBase.Api.OpenApi;
using ArquitecturaBase.Application.Interfaces.Services;
using ArquitecturaBase.Application.Models.Settings;
using ArquitecturaBase.Domain.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Authorization;

namespace ArquitecturaBase.Api.Controllers;

[ApiController]
[Route("api/settings")]
[Tags("Settings")]
public sealed class SettingsController(ISystemSettingsService service) : ControllerBase
{
    [HttpGet]
    [HasPermission(Permissions.Settings.Manage)]
    [ProducesProblem(StatusCodes.Status404NotFound)]
    [ProducesResponseType<SystemSettingsResponse>(StatusCodes.Status200OK)]
    public async Task<IActionResult> Get(CancellationToken cancellationToken) =>
        (await service.GetAsync(cancellationToken)).ToActionResult(this);

    [HttpPut]
    [HasPermission(Permissions.Settings.Manage)]
    [ProducesProblem(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> Update(
        [FromBody] UpdateSystemSettingsHttpRequest request,
        CancellationToken cancellationToken) =>
        (await service.UpdateAsync(new UpdateSystemSettingsRequest(request.RegistrationMode), cancellationToken))
            .ToActionResult(this);

    [HttpGet("presentation")]
    [AllowAnonymous]
    [ProducesResponseType<SystemPresentationResponse>(StatusCodes.Status200OK)]
    public async Task<IActionResult> GetPresentation(CancellationToken cancellationToken) =>
        (await service.GetPresentationAsync(cancellationToken)).ToActionResult(this);

    [HttpPatch]
    [HasPermission(Permissions.Settings.Manage)]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesProblem(StatusCodes.Status404NotFound)]
    [ProducesProblem(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> UpdateSection(
        [FromBody] UpdateSystemSettingsSectionHttpRequest request,
        CancellationToken cancellationToken) =>
        (await service.UpdateSectionAsync(new UpdateSystemSettingsSectionRequest(
            request.ExpectedRevision, request.DefaultCulture, request.DefaultTimeZoneId,
            request.DefaultPageSize, request.RegistrationMode), cancellationToken)).ToActionResult(this);
}
