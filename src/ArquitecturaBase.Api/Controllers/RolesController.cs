using ArquitecturaBase.Api.Authorization;
using ArquitecturaBase.Api.ErrorHandling;
using ArquitecturaBase.Application.Interfaces.Services;
using ArquitecturaBase.Domain.Authorization;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace ArquitecturaBase.Api.Controllers;

[ApiController]
[Route("api/roles")]
[Tags("Roles")]
public sealed class RolesController(IRoleService service) : ControllerBase
{
    [HttpGet]
    [Authorize(Policy = PermissionPolicyProvider.PolicyPrefix + Permissions.Roles.Read)]
    public async Task<IActionResult> List(CancellationToken cancellationToken) =>
        (await service.GetRolesAsync(cancellationToken)).ToActionResult(this);
}
