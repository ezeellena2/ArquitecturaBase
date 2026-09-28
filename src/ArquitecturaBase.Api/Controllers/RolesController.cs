using ArquitecturaBase.Api.Authorization;
using ArquitecturaBase.Api.Contracts.Roles;
using ArquitecturaBase.Api.ErrorHandling;
using ArquitecturaBase.Api.OpenApi;
using ArquitecturaBase.Application.Interfaces.Services;
using ArquitecturaBase.Application.Models.Roles;
using ArquitecturaBase.Domain.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace ArquitecturaBase.Api.Controllers;

[ApiController]
[Route("api/roles")]
[Tags("Roles")]
public sealed class RolesController(IRoleService service) : ControllerBase
{
    [HttpGet]
    [HasPermission(Permissions.Roles.Read)]
    [ProducesResponseType<IReadOnlyCollection<RoleResponse>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> List(CancellationToken cancellationToken) =>
        (await service.GetRolesAsync(cancellationToken)).ToActionResult(this);

    // El alta de un rol sigue respondiendo 200 con el id, y no 201 como la de un usuario: todavía no hay un
    // GET /api/roles/{id} al que pueda apuntar el Location (llega en la Etapa 4). Cuando exista, pasa a ToCreatedResult.
    [HttpPost]
    [HasPermission(Permissions.Roles.Manage)]
    [ProducesResponseType<Guid>(StatusCodes.Status200OK)]
    // Otro rol con el mismo nombre.
    [ProducesProblem(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Create([FromBody] CreateRoleHttpRequest request, CancellationToken cancellationToken) =>
        (await service.CreateAsync(
            new CreateRoleRequest(request.Name, request.Description, request.Permissions), cancellationToken))
            .ToActionResult(this);

    [HttpPut("{id:guid}")]
    [HasPermission(Permissions.Roles.Manage)]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    // Otro rol con el mismo nombre.
    [ProducesProblem(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Update(
        [FromRoute] Guid id,
        [FromBody] UpdateRoleHttpRequest request,
        CancellationToken cancellationToken) =>
        (await service.UpdateAsync(
            new UpdateRoleRequest(id, request.Name, request.Description, request.Permissions), cancellationToken))
            .ToActionResult(this);

    [HttpDelete("{id:guid}")]
    [HasPermission(Permissions.Roles.Manage)]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    // El rol todavía tiene usuarios.
    [ProducesProblem(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Delete([FromRoute] Guid id, CancellationToken cancellationToken) =>
        (await service.DeleteAsync(new DeleteRoleRequest(id), cancellationToken)).ToActionResult(this);
}
