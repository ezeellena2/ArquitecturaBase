using ArquitecturaBase.Api.Authorization;
using ArquitecturaBase.Api.ErrorHandling;
using ArquitecturaBase.Application.Abstractions.Messaging;
using ArquitecturaBase.Application.Features.Roles.CreateRole;
using ArquitecturaBase.Application.Features.Roles.DeleteRole;
using ArquitecturaBase.Application.Features.Roles.UpdateRole;
using ArquitecturaBase.Domain.Authorization;

namespace ArquitecturaBase.Api.Endpoints.Roles;

/// <summary>Roles y catálogo de permisos (sección 10 del spec de la Fase 4).</summary>
internal sealed class RolesEndpoints : IEndpoint
{
    public void MapEndpoint(IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/roles").WithTags("Roles");

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
    }
}

/// <summary>El cuerpo de PUT /api/roles/{id}: el id va en la ruta, no en el JSON.</summary>
public sealed record UpdateRoleRequest(string? Name, string? Description, IReadOnlyCollection<string>? Permissions);
