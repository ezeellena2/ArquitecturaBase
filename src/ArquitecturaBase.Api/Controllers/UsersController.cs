using ArquitecturaBase.Api.Authorization;
using ArquitecturaBase.Api.Contracts.Users;
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
public sealed class UsersController(IUserService service) : ControllerBase
{
    [HttpGet]
    [Authorize(Policy = PermissionPolicyProvider.PolicyPrefix + Permissions.Users.Read)]
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
    [Authorize(Policy = PermissionPolicyProvider.PolicyPrefix + Permissions.Users.Read)]
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
    [Authorize(Policy = PermissionPolicyProvider.PolicyPrefix + Permissions.Users.Read)]
    public async Task<IActionResult> Get([FromRoute] Guid id, CancellationToken cancellationToken) =>
        (await service.GetUserAsync(id, cancellationToken)).ToActionResult(this);

    [HttpPost]
    [Authorize(Policy = PermissionPolicyProvider.PolicyPrefix + Permissions.Users.Manage)]
    [ProducesResponseType(typeof(Guid), StatusCodes.Status200OK)]
    public async Task<IActionResult> Create([FromBody] CreateUserHttpRequest request, CancellationToken cancellationToken) =>
        (await service.CreateUserAsync(new CreateUserRequest(
            request.Email,
            request.DisplayName,
            request.Roles,
            request.Phone is null ? null : new PhoneNumberInput(request.Phone.Country, request.Phone.Number),
            request.Invitation is null ? null : new InvitationRequest(request.Invitation.Channel, request.Invitation.Consent)),
            cancellationToken)).ToActionResult(this);

    [HttpPut("{id:guid}")]
    [Authorize(Policy = PermissionPolicyProvider.PolicyPrefix + Permissions.Users.Manage)]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> Update(
        [FromRoute] Guid id,
        [FromBody] UpdateUserHttpRequest request,
        CancellationToken cancellationToken) =>
        (await service.UpdateUserAsync(new UpdateUserRequest(
            id,
            request.DisplayName,
            request.Roles,
            request.Email,
            request.Phone is null ? null : new PhoneNumberInput(request.Phone.Country, request.Phone.Number)),
            cancellationToken)).ToActionResult(this);

    [HttpPost("{id:guid}/invitation")]
    [Authorize(Policy = PermissionPolicyProvider.PolicyPrefix + Permissions.Users.Manage)]
    [ProducesResponseType(StatusCodes.Status202Accepted)]
    public async Task<IActionResult> SendInvitation(
        [FromRoute] Guid id,
        [FromBody] SendInvitationHttpRequest request,
        CancellationToken cancellationToken)
    {
        var result = await service.SendInvitationAsync(
            new SendUserInvitationRequest(id, request.Channel, request.Consent), cancellationToken);
        return result.IsSuccess ? StatusCode(StatusCodes.Status202Accepted) : result.ToActionResult(this);
    }

    // MVC convierte role= a null; el endpoint anterior conservaba la cadena vacía para validarla como 400.
    private string? RoleFilter(string? role) =>
        role ?? (Request.Query.ContainsKey("role") ? string.Empty : null);
}
