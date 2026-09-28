using ArquitecturaBase.Api.Authorization;
using ArquitecturaBase.Api.ErrorHandling;
using ArquitecturaBase.Application.Interfaces.Services;
using ArquitecturaBase.Application.Models.Roles;
using ArquitecturaBase.Domain.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace ArquitecturaBase.Api.Controllers;

[ApiController]
[Route("api/permissions")]
[Tags("Roles")]
[HasPermission(Permissions.Roles.Read)]
public sealed class PermissionsController(IRoleService service) : ControllerBase
{
    [HttpGet]
    [ProducesResponseType<IReadOnlyCollection<PermissionGroupResponse>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> List(CancellationToken cancellationToken) =>
        (await service.GetPermissionsAsync(cancellationToken)).ToActionResult(this);
}
