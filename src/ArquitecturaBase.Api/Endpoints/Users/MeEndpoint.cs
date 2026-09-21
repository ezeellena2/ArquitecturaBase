using ArquitecturaBase.Api.ErrorHandling;
using ArquitecturaBase.Application.Abstractions.Messaging;
using ArquitecturaBase.Application.Features.Users.GetCurrentUser;
using ArquitecturaBase.Application.Features.Users.UpdateProfile;

namespace ArquitecturaBase.Api.Endpoints.Users;

/// <summary>
/// El propio usuario: perfil, roles, permisos, idioma, zona horaria y último ingreso (sección 5.6), y la edición
/// de su perfil (sección 9 del spec de la Fase 4). No pide permisos: alcanza con el bearer.
/// </summary>
internal sealed class MeEndpoint : IEndpoint
{
    public void MapEndpoint(IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/me").RequireAuthorization().WithTags("Users");

        group.MapGet("", async (
                IQueryHandler<GetCurrentUserQuery, CurrentUserResponse> handler,
                CancellationToken cancellationToken) =>
            (await handler.Handle(new GetCurrentUserQuery(), cancellationToken)).ToHttpResult());

        group.MapPut("", async (
                UpdateProfileCommand command,
                ICommandHandler<UpdateProfileCommand> handler,
                CancellationToken cancellationToken) =>
            (await handler.Handle(command, cancellationToken)).ToHttpResult());
    }
}
