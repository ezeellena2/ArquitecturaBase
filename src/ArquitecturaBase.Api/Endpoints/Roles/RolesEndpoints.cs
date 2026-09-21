using ArquitecturaBase.Api.Authorization;
using ArquitecturaBase.Api.ErrorHandling;
using ArquitecturaBase.Application.Abstractions.Messaging;
using ArquitecturaBase.Application.Features.Roles.CreateRole;
using ArquitecturaBase.Application.Features.Roles.DeleteRole;
using ArquitecturaBase.Application.Features.Roles.GetPermissions;
using ArquitecturaBase.Application.Features.Roles.GetRoles;
using ArquitecturaBase.Application.Features.Roles.UpdateRole;
using ArquitecturaBase.Domain.Authorization;

namespace ArquitecturaBase.Api.Endpoints.Roles;

/// <summary>Roles y catálogo de permisos (sección 10 del spec de la Fase 4).</summary>
internal sealed class RolesEndpoints : IEndpoint
{
    public void MapEndpoint(IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/roles").WithTags("Roles");

        group.MapGet("", async (
                IQueryHandler<GetRolesQuery, IReadOnlyCollection<RoleListItem>> handler,
                CancellationToken cancellationToken) =>
            (await handler.Handle(new GetRolesQuery(), cancellationToken)).ToHttpResult())
            .RequirePermission(Permissions.Roles.Read);

        group.MapPost("", async (
                CreateRoleCommand command,
                ICommandHandler<CreateRoleCommand, Guid> handler,
                CancellationToken cancellationToken) =>
            (await handler.Handle(command, cancellationToken)).ToHttpResult())
            .RequirePermission(Permissions.Roles.Manage);

        group.MapPut("/{id:guid}", async (
                Guid id,
                UpdateRoleRequest request,
                ICommandHandler<UpdateRoleCommand> handler,
                CancellationToken cancellationToken) =>
            (await handler.Handle(
                new UpdateRoleCommand(id, request.Name, request.Description, request.Permissions),
                cancellationToken)).ToHttpResult())
            .RequirePermission(Permissions.Roles.Manage);

        group.MapDelete("/{id:guid}", async (
                Guid id,
                ICommandHandler<DeleteRoleCommand> handler,
                CancellationToken cancellationToken) =>
            (await handler.Handle(new DeleteRoleCommand(id), cancellationToken)).ToHttpResult())
            .RequirePermission(Permissions.Roles.Manage);

        // El catálogo es lo que la pantalla de roles necesita para dibujar las casillas, así que pide el mismo permiso.
        app.MapGet("/api/permissions", async (
                IQueryHandler<GetPermissionsQuery, IReadOnlyCollection<PermissionGroup>> handler,
                CancellationToken cancellationToken) =>
            (await handler.Handle(new GetPermissionsQuery(), cancellationToken)).ToHttpResult())
            .RequirePermission(Permissions.Roles.Read)
            .WithTags("Roles");
    }
}

/// <summary>El cuerpo de PUT /api/roles/{id}: el id va en la ruta, no en el JSON.</summary>
public sealed record UpdateRoleRequest(string? Name, string? Description, IReadOnlyCollection<string>? Permissions);
