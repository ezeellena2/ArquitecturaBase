using ArquitecturaBase.Api.Authorization;
using ArquitecturaBase.Api.Contracts.Roles;
using ArquitecturaBase.Api.ErrorHandling;
using ArquitecturaBase.Application.Interfaces.Services;
using ArquitecturaBase.Application.Models.Roles;
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

    [HttpPost]
    [Authorize(Policy = PermissionPolicyProvider.PolicyPrefix + Permissions.Roles.Manage)]
    public async Task<IActionResult> Create([FromBody] CreateRoleHttpRequest request, CancellationToken cancellationToken) =>
        (await service.CreateAsync(
            new CreateRoleRequest(request.Name, request.Description, request.Permissions), cancellationToken))
            .ToActionResult(this);

    [HttpPut("{id:guid}")]
    [Authorize(Policy = PermissionPolicyProvider.PolicyPrefix + Permissions.Roles.Manage)]
    public async Task<IActionResult> Update(
        [FromRoute] Guid id,
        [FromBody] UpdateRoleHttpRequest request,
        CancellationToken cancellationToken) =>
        (await service.UpdateAsync(
            new UpdateRoleRequest(id, request.Name, request.Description, request.Permissions), cancellationToken))
            .ToActionResult(this);

    [HttpDelete("{id:guid}")]
    [Authorize(Policy = PermissionPolicyProvider.PolicyPrefix + Permissions.Roles.Manage)]
    public async Task<IActionResult> Delete([FromRoute] Guid id, CancellationToken cancellationToken) =>
        (await service.DeleteAsync(new DeleteRoleRequest(id), cancellationToken)).ToActionResult(this);
}
