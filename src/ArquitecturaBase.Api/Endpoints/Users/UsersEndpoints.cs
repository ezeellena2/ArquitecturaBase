using ArquitecturaBase.Api.Authorization;
using ArquitecturaBase.Api.ErrorHandling;
using ArquitecturaBase.Application.Abstractions.Messaging;
using ArquitecturaBase.Application.Features.Users.DeleteUser;
using ArquitecturaBase.Application.Features.Users.SendInvitation;
using ArquitecturaBase.Application.Features.Users.SetUserActive;
using ArquitecturaBase.Application.Features.Users.UnlinkUserPhone;
using ArquitecturaBase.Domain.Authorization;
using ArquitecturaBase.Domain.Users;

namespace ArquitecturaBase.Api.Endpoints.Users;

/// <summary>
/// Escrituras de usuarios que aún usan endpoints heredados. Desde el ingreso con WhatsApp (sección 12 de su spec),
/// el alta y la edición cargan un correo o un número, y el admin puede reenviar la invitación y desvincular WhatsApp.
/// </summary>
internal sealed class UsersEndpoints : IEndpoint
{
    public void MapEndpoint(IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/users").WithTags("Users");

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

/// <summary>El cuerpo de POST /api/users/{id}/invitation: por dónde y, para WhatsApp, el consentimiento.</summary>
public sealed record SendInvitationRequest(UserInvitationChannel? Channel, bool Consent);
