using ArquitecturaBase.Api.Authorization;
using ArquitecturaBase.Api.Contracts.Settings;
using ArquitecturaBase.Api.ErrorHandling;
using ArquitecturaBase.Api.OpenApi;
using ArquitecturaBase.Application.Interfaces.Services;
using ArquitecturaBase.Application.Models.Settings;
using ArquitecturaBase.Domain.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace ArquitecturaBase.Api.Controllers;

[ApiController]
[Route("api/settings")]
[Tags("Settings")]
[HasPermission(Permissions.Settings.Manage)]
// Las dos leen la fila única de ajustes: si falta (una base sin el seed), responden 404.
[ProducesProblem(StatusCodes.Status404NotFound)]
public sealed class SettingsController(ISystemSettingsService service) : ControllerBase
{
    [HttpGet]
    [ProducesResponseType<SystemSettingsResponse>(StatusCodes.Status200OK)]
    public async Task<IActionResult> Get(CancellationToken cancellationToken) =>
        (await service.GetAsync(cancellationToken)).ToActionResult(this);

    [HttpPut]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> Update(
        [FromBody] UpdateSystemSettingsHttpRequest request,
        CancellationToken cancellationToken) =>
        (await service.UpdateAsync(new UpdateSystemSettingsRequest(request.RegistrationMode), cancellationToken))
            .ToActionResult(this);
}
