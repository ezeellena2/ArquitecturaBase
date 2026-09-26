using ArquitecturaBase.Api.Authorization;
using ArquitecturaBase.Api.Contracts.Settings;
using ArquitecturaBase.Api.ErrorHandling;
using ArquitecturaBase.Application.Interfaces.Services;
using ArquitecturaBase.Application.Models.Settings;
using ArquitecturaBase.Domain.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace ArquitecturaBase.Api.Controllers;

[ApiController]
[Route("api/settings")]
[Tags("Settings")]
[HasPermission(Permissions.Settings.Manage)]
public sealed class SettingsController(ISystemSettingsService service) : ControllerBase
{
    [HttpGet]
    public async Task<IActionResult> Get(CancellationToken cancellationToken) =>
        (await service.GetAsync(cancellationToken)).ToActionResult(this);

    [HttpPut]
    public async Task<IActionResult> Update(
        [FromBody] UpdateSystemSettingsHttpRequest request,
        CancellationToken cancellationToken) =>
        (await service.UpdateAsync(new UpdateSystemSettingsRequest(request.RegistrationMode), cancellationToken))
            .ToActionResult(this);
}
