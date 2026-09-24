using ArquitecturaBase.Api.Authorization;
using ArquitecturaBase.Api.ErrorHandling;
using ArquitecturaBase.Application.Common.Pagination;
using ArquitecturaBase.Application.Interfaces.Services;
using ArquitecturaBase.Application.Models.Users;
using ArquitecturaBase.Domain.Authorization;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace ArquitecturaBase.Api.Controllers;

[ApiController]
[Route("api/users")]
[Tags("Users")]
[Authorize(Policy = PermissionPolicyProvider.PolicyPrefix + Permissions.Users.Read)]
public sealed class UsersController(IUserService service) : ControllerBase
{
    [HttpGet]
    public async Task<IActionResult> List(
        [FromQuery] int? page,
        [FromQuery] int? pageSize,
        [FromQuery] string? sort,
        [FromQuery] string? search,
        [FromQuery] bool? isActive,
        [FromQuery] string? role,
        [FromQuery] int? createdWithinDays,
        CancellationToken cancellationToken) =>
        (await service.ListUsersAsync(new ListUsersRequest
        {
            Page = page ?? PagedRequest.DefaultPage,
            PageSize = pageSize ?? PagedRequest.DefaultPageSize,
            Sort = sort,
            Search = search,
            IsActive = isActive,
            Role = RoleFilter(role),
            CreatedWithinDays = createdWithinDays,
        }, cancellationToken)).ToActionResult(this);

    [HttpGet("filter-counts")]
    public async Task<IActionResult> FilterCounts(
        [FromQuery] string? search,
        [FromQuery] bool? isActive,
        [FromQuery] string? role,
        [FromQuery] int? createdWithinDays,
        CancellationToken cancellationToken) =>
        (await service.GetUserFilterCountsAsync(new UserFilterCountsRequest
        {
            Search = search,
            IsActive = isActive,
            Role = RoleFilter(role),
            CreatedWithinDays = createdWithinDays,
        }, cancellationToken)).ToActionResult(this);

    [HttpGet("{id:guid}")]
    public async Task<IActionResult> Get([FromRoute] Guid id, CancellationToken cancellationToken) =>
        (await service.GetUserAsync(id, cancellationToken)).ToActionResult(this);

    // MVC convierte role= a null; el endpoint anterior conservaba la cadena vacía para validarla como 400.
    private string? RoleFilter(string? role) =>
        role ?? (Request.Query.ContainsKey("role") ? string.Empty : null);
}
