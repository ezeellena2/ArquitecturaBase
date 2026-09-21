using ArquitecturaBase.Api.Authorization;
using ArquitecturaBase.Api.ErrorHandling;
using ArquitecturaBase.Application.Abstractions.Messaging;
using ArquitecturaBase.Application.Features.Settings.GetSystemSettings;
using ArquitecturaBase.Application.Features.Settings.UpdateSystemSettings;
using ArquitecturaBase.Domain.Authorization;

namespace ArquitecturaBase.Api.Endpoints.Settings;

/// <summary>Configuración del sistema (sección 10 del spec de la Fase 4). Solo la ve quien tiene settings.manage.</summary>
internal sealed class SettingsEndpoints : IEndpoint
{
    public void MapEndpoint(IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/settings").WithTags("Settings");

        group.MapGet("", async (
                IQueryHandler<GetSystemSettingsQuery, SystemSettingsResponse> handler,
                CancellationToken cancellationToken) =>
            (await handler.Handle(new GetSystemSettingsQuery(), cancellationToken)).ToHttpResult())
            .RequirePermission(Permissions.Settings.Manage);

        group.MapPut("", async (
                UpdateSystemSettingsCommand command,
                ICommandHandler<UpdateSystemSettingsCommand> handler,
                CancellationToken cancellationToken) =>
            (await handler.Handle(command, cancellationToken)).ToHttpResult())
            .RequirePermission(Permissions.Settings.Manage);
    }
}
