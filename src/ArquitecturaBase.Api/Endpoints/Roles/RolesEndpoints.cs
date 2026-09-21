using ArquitecturaBase.Api.Authorization;
using ArquitecturaBase.Api.ErrorHandling;
using ArquitecturaBase.Application.Abstractions.Messaging;
using ArquitecturaBase.Application.Features.Roles.GetPermissions;
using ArquitecturaBase.Application.Features.Roles.GetRoles;
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

        // El catálogo es lo que la pantalla de roles necesita para dibujar las casillas, así que pide el mismo permiso.
        app.MapGet("/api/permissions", async (
                IQueryHandler<GetPermissionsQuery, IReadOnlyCollection<PermissionGroup>> handler,
                CancellationToken cancellationToken) =>
            (await handler.Handle(new GetPermissionsQuery(), cancellationToken)).ToHttpResult())
            .RequirePermission(Permissions.Roles.Read)
            .WithTags("Roles");
    }
}
