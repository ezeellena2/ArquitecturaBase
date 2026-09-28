using ArquitecturaBase.Api.Authorization;
using ArquitecturaBase.Api.Contracts.Roles;
using ArquitecturaBase.Api.ErrorHandling;
using ArquitecturaBase.Api.OpenApi;
using ArquitecturaBase.Application.Common.Pagination;
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

    // El listado paginado. Es una ruta aparte y no una query opcional de GET /api/roles, que sigue siendo el catálogo
    // completo de los selectores: una operación no puede declarar dos esquemas de respuesta (ADR 0004, enmienda).
    [HttpGet("paged")]
    [HasPermission(Permissions.Roles.Read)]
    [ProducesResponseType<PagedResult<RoleResponse>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> ListPaged(
        [FromQuery] int? page,
        [FromQuery] int? pageSize,
        [FromQuery] string? sort,
        [FromQuery] string? search,
        CancellationToken cancellationToken) =>
        (await service.ListRolesAsync(new ListRolesRequest
        {
            Page = page ?? PagedRequest.DefaultPage,
            PageSize = pageSize ?? PagedRequest.DefaultPageSize,
            Sort = sort,
            Search = search,
        }, cancellationToken)).ToActionResult(this);

    [HttpGet("{id:guid}")]
    [HasPermission(Permissions.Roles.Read)]
    [ProducesResponseType<RoleResponse>(StatusCodes.Status200OK)]
    public async Task<IActionResult> Get([FromRoute] Guid id, CancellationToken cancellationToken) =>
        (await service.GetRoleAsync(id, cancellationToken)).ToActionResult(this);

    [HttpPost]
    [HasPermission(Permissions.Roles.Manage)]
    [ProducesResponseType<Guid>(StatusCodes.Status201Created)]
    // Otro rol con el mismo nombre.
    [ProducesProblem(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Create([FromBody] CreateRoleHttpRequest request, CancellationToken cancellationToken) =>
        (await service.CreateAsync(
            new CreateRoleRequest(request.Name, request.Description, request.Permissions), cancellationToken))
            .ToCreatedResult(this, nameof(Get), id => new { id });

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
