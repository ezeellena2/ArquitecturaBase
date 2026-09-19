using ArquitecturaBase.Api.ErrorHandling;
using ArquitecturaBase.Application.Abstractions.Messaging;
using ArquitecturaBase.Application.Features.Users.GetCurrentUser;

namespace ArquitecturaBase.Api.Endpoints.Users;

/// <summary>Perfil, roles, permisos e idioma/zona horaria del usuario del token (sección 5.6).</summary>
internal sealed class MeEndpoint : IEndpoint
{
    public void MapEndpoint(IEndpointRouteBuilder app) =>
        app.MapGet("/api/me", async (
                IQueryHandler<GetCurrentUserQuery, CurrentUserResponse> handler,
                CancellationToken cancellationToken) =>
            (await handler.Handle(new GetCurrentUserQuery(), cancellationToken)).ToHttpResult())
            .RequireAuthorization()
            .WithTags("Users");
}
