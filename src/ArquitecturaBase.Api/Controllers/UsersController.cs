using ArquitecturaBase.Api.Authorization;
using ArquitecturaBase.Api.Contracts.Users;
using ArquitecturaBase.Api.ErrorHandling;
using ArquitecturaBase.Api.OpenApi;
using ArquitecturaBase.Application.Common.Pagination;
using ArquitecturaBase.Application.Interfaces.Services;
using ArquitecturaBase.Application.Models.Users;
using ArquitecturaBase.Domain.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace ArquitecturaBase.Api.Controllers;

[ApiController]
[Route("api/users")]
[Tags("Users")]
public sealed class UsersController(
    IUserQueryService queries,
    IUserAdministrationService administration,
    IUserAccessService access) : ControllerBase
{
    [HttpGet]
    [HasPermission(Permissions.Users.Read)]
    [ProducesResponseType<PagedResult<UserListItemResponse>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> List(
        [FromQuery] int? page,
        [FromQuery] int? pageSize,
        [FromQuery] string? sort,
        [FromQuery] string? search,
        [FromQuery] bool? isActive,
        [FromQuery] string? role,
        [FromQuery] int? createdWithinDays,
        CancellationToken cancellationToken) =>
        (await queries.ListUsersAsync(new ListUsersRequest
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
    [HasPermission(Permissions.Users.Read)]
    [ProducesResponseType<UserFilterCounts>(StatusCodes.Status200OK)]
    public async Task<IActionResult> FilterCounts(
        [FromQuery] string? search,
        [FromQuery] bool? isActive,
        [FromQuery] string? role,
        [FromQuery] int? createdWithinDays,
        CancellationToken cancellationToken) =>
        (await queries.GetUserFilterCountsAsync(new ListUsersRequest
        {
            Search = search,
            IsActive = isActive,
            Role = RoleFilter(role),
            CreatedWithinDays = createdWithinDays,
        }, cancellationToken)).ToActionResult(this);

    [HttpGet("{id:guid}")]
    [HasPermission(Permissions.Users.Read)]
    [ProducesResponseType<UserDetailResponse>(StatusCodes.Status200OK)]
    public async Task<IActionResult> Get([FromRoute] Guid id, CancellationToken cancellationToken) =>
        (await queries.GetUserAsync(id, cancellationToken)).ToActionResult(this);

    [HttpPost]
    [HasPermission(Permissions.Users.Manage)]
    [ProducesResponseType<Guid>(StatusCodes.Status201Created)]
    // Un rol pedido que no existe responde 404, aunque la ruta no nombre un recurso; el correo o el número de otra cuenta,
    // 409.
    [ProducesProblem(StatusCodes.Status404NotFound)]
    [ProducesProblem(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Create([FromBody] CreateUserHttpRequest request, CancellationToken cancellationToken) =>
        (await administration.CreateUserAsync(new CreateUserRequest(
            request.Email,
            request.DisplayName,
            request.Roles,
            request.Phone is null ? null : new PhoneNumberInput(request.Phone.Country, request.Phone.Number),
            request.Invitation is null ? null : new InvitationRequest(request.Invitation.Channel, request.Invitation.Consent)),
            cancellationToken)).ToCreatedResult(this, nameof(Get), id => new { id });

    [HttpPut("{id:guid}")]
    [HasPermission(Permissions.Users.Manage)]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    // El correo o el número de otra cuenta, quitarse a uno mismo el rol Admin o quitárselo al último administrador.
    [ProducesProblem(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Update(
        [FromRoute] Guid id,
        [FromBody] UpdateUserHttpRequest request,
        CancellationToken cancellationToken) =>
        (await administration.UpdateUserAsync(new UpdateUserRequest(
            id,
            request.DisplayName,
            request.Roles,
            request.Email,
            request.Phone is null ? null : new PhoneNumberInput(request.Phone.Country, request.Phone.Number)),
            cancellationToken)).ToActionResult(this);

    [HttpPost("{id:guid}/invitation")]
    [HasPermission(Permissions.Users.Manage)]
    [ProducesResponseType(StatusCodes.Status202Accepted)]
    // Sin [EnableRateLimiting]: el 429 es de la espera entre dos invitaciones de la misma cuenta.
    [ProducesProblem(StatusCodes.Status429TooManyRequests)]
    public async Task<IActionResult> SendInvitation(
        [FromRoute] Guid id,
        [FromBody] SendInvitationHttpRequest request,
        CancellationToken cancellationToken) =>
        (await administration.SendInvitationAsync(
            new SendUserInvitationRequest(id, request.Channel, request.Consent), cancellationToken)).ToAcceptedResult(this);

    [HttpPost("{id:guid}/activate")]
    [HasPermission(Permissions.Users.Manage)]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> Activate([FromRoute] Guid id, CancellationToken cancellationToken) =>
        (await access.SetUserActiveAsync(id, isActive: true, cancellationToken)).ToActionResult(this);

    [HttpPost("{id:guid}/deactivate")]
    [HasPermission(Permissions.Users.Manage)]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    // La propia cuenta o el último administrador activo. Activar no tiene 409.
    [ProducesProblem(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Deactivate([FromRoute] Guid id, CancellationToken cancellationToken) =>
        (await access.SetUserActiveAsync(id, isActive: false, cancellationToken)).ToActionResult(this);

    [HttpDelete("{id:guid}")]
    [HasPermission(Permissions.Users.Manage)]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    // La propia cuenta o el último administrador activo.
    [ProducesProblem(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Delete([FromRoute] Guid id, CancellationToken cancellationToken) =>
        (await access.DeleteUserAsync(id, cancellationToken)).ToActionResult(this);

    [HttpDelete("{id:guid}/whatsapp")]
    [HasPermission(Permissions.Users.Manage)]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    // El único medio de ingreso de la propia cuenta.
    [ProducesProblem(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> UnlinkPhone([FromRoute] Guid id, CancellationToken cancellationToken) =>
        (await access.UnlinkUserPhoneAsync(id, cancellationToken)).ToActionResult(this);

    // MVC convierte role= a null; se recupera la cadena vacía para que el validador responda 400: un role= presente y
    // vacío no significa "sin filtro".
    private string? RoleFilter(string? role) =>
        role ?? (Request.Query.ContainsKey("role") ? string.Empty : null);
}
