using ArquitecturaBase.Api.Authorization;
using ArquitecturaBase.Api.ErrorHandling;
using ArquitecturaBase.Application.Abstractions.Messaging;
using ArquitecturaBase.Application.Common.Pagination;
using ArquitecturaBase.Application.Features.Users;
using ArquitecturaBase.Application.Features.Users.CreateUser;
using ArquitecturaBase.Application.Features.Users.DeleteUser;
using ArquitecturaBase.Application.Models.Identity;
using ArquitecturaBase.Application.Features.Users.GetUser;
using ArquitecturaBase.Application.Features.Users.GetUserFilterCounts;
using ArquitecturaBase.Application.Features.Users.GetUsers;
using ArquitecturaBase.Application.Features.Users.SendInvitation;
using ArquitecturaBase.Application.Features.Users.SetUserActive;
using ArquitecturaBase.Application.Features.Users.UnlinkUserPhone;
using ArquitecturaBase.Application.Features.Users.UpdateUser;
using ArquitecturaBase.Domain.Authorization;
using ArquitecturaBase.Domain.Users;

namespace ArquitecturaBase.Api.Endpoints.Users;

/// <summary>
/// ABM de usuarios (sección 10 del spec de la Fase 4). El listado es el ejemplo del patrón de paginado
/// (sección 6.2): los parámetros se enlazan a mano porque [AsParameters] haría obligatorios los int de PagedRequest.
/// Desde el ingreso con WhatsApp (sección 12 de su spec), el alta y la edición cargan un correo o un número, el admin
/// puede reenviar la invitación y desvincular el WhatsApp de alguien.
/// </summary>
internal sealed class UsersEndpoints : IEndpoint
{
    public void MapEndpoint(IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/users").WithTags("Users");

        group.MapGet("", async (
                int? page,
                int? pageSize,
                string? sort,
                string? search,
                bool? isActive,
                string? role,
                int? createdWithinDays,
                IQueryHandler<GetUsersQuery, PagedResult<UserListItem>> handler,
                CancellationToken cancellationToken) =>
            (await handler.Handle(
                new GetUsersQuery
                {
                    Page = page ?? PagedRequest.DefaultPage,
                    PageSize = pageSize ?? PagedRequest.DefaultPageSize,
                    Sort = sort,
                    Search = search,
                    IsActive = isActive,
                    Role = role,
                    CreatedWithinDays = createdWithinDays,
                },
                cancellationToken)).ToHttpResult())
            .RequirePermission(Permissions.Users.Read);

        // Antes de "/{id:guid}" no hace falta cuidarse: la restricción de ruta no matchea "filter-counts".
        group.MapGet("/filter-counts", async (
                string? search,
                bool? isActive,
                string? role,
                int? createdWithinDays,
                IQueryHandler<GetUserFilterCountsQuery, UserFilterCounts> handler,
                CancellationToken cancellationToken) =>
            (await handler.Handle(
                new GetUserFilterCountsQuery
                {
                    Search = search,
                    IsActive = isActive,
                    Role = role,
                    CreatedWithinDays = createdWithinDays,
                },
                cancellationToken)).ToHttpResult())
            .RequirePermission(Permissions.Users.Read);

        group.MapPost("", async (
                CreateUserCommand command,
                ICommandHandler<CreateUserCommand, Guid> handler,
                CancellationToken cancellationToken) =>
            (await handler.Handle(command, cancellationToken)).ToHttpResult())
            .RequirePermission(Permissions.Users.Manage);

        group.MapGet("/{id:guid}", async (
                Guid id,
                IQueryHandler<GetUserQuery, UserDetail> handler,
                CancellationToken cancellationToken) =>
            (await handler.Handle(new GetUserQuery(id), cancellationToken)).ToHttpResult())
            .RequirePermission(Permissions.Users.Read);

        group.MapPut("/{id:guid}", async (
                Guid id,
                UpdateUserRequest request,
                ICommandHandler<UpdateUserCommand> handler,
                CancellationToken cancellationToken) =>
            (await handler.Handle(
                new UpdateUserCommand(id, request.DisplayName, request.Roles, request.Email, request.Phone),
                cancellationToken)).ToHttpResult())
            .RequirePermission(Permissions.Users.Manage);

        // 202: la invitación sale en segundo plano, por la cola de correos o la de WhatsApp.
        group.MapPost("/{id:guid}/invitation", async (
                Guid id,
                SendInvitationRequest request,
                ICommandHandler<SendInvitationCommand> handler,
                CancellationToken cancellationToken) =>
            {
                var result = await handler.Handle(new SendInvitationCommand(id, request.Channel, request.Consent), cancellationToken);

                return result.IsSuccess ? TypedResults.Accepted((string?)null) : result.Error.ToProblem();
            })
            .RequirePermission(Permissions.Users.Manage);

        group.MapDelete("/{id:guid}/whatsapp", async (
                Guid id,
                ICommandHandler<UnlinkUserPhoneCommand> handler,
                CancellationToken cancellationToken) =>
            (await handler.Handle(new UnlinkUserPhoneCommand(id), cancellationToken)).ToHttpResult())
            .RequirePermission(Permissions.Users.Manage);

        group.MapPost("/{id:guid}/activate", async (
                Guid id,
                ICommandHandler<SetUserActiveCommand> handler,
                CancellationToken cancellationToken) =>
            (await handler.Handle(new SetUserActiveCommand(id, IsActive: true), cancellationToken)).ToHttpResult())
            .RequirePermission(Permissions.Users.Manage);

        group.MapPost("/{id:guid}/deactivate", async (
                Guid id,
                ICommandHandler<SetUserActiveCommand> handler,
                CancellationToken cancellationToken) =>
            (await handler.Handle(new SetUserActiveCommand(id, IsActive: false), cancellationToken)).ToHttpResult())
            .RequirePermission(Permissions.Users.Manage);

        group.MapDelete("/{id:guid}", async (
                Guid id,
                ICommandHandler<DeleteUserCommand> handler,
                CancellationToken cancellationToken) =>
            (await handler.Handle(new DeleteUserCommand(id), cancellationToken)).ToHttpResult())
            .RequirePermission(Permissions.Users.Manage);
    }
}

/// <summary>
/// El cuerpo de PUT /api/users/{id}: el id va en la ruta, no en el JSON. El correo y el número son opcionales: ausentes
/// o null, no cambian.
/// </summary>
public sealed record UpdateUserRequest(
    string? DisplayName,
    IReadOnlyCollection<string>? Roles,
    string? Email = null,
    PhoneNumberInput? Phone = null);

/// <summary>El cuerpo de POST /api/users/{id}/invitation: por dónde y, para WhatsApp, el consentimiento.</summary>
public sealed record SendInvitationRequest(UserInvitationChannel? Channel, bool Consent);
