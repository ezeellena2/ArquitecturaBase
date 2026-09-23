using ArquitecturaBase.Api.ErrorHandling;
using ArquitecturaBase.Application.Abstractions.Messaging;
using ArquitecturaBase.Application.Features.Auth.GetLoginMethods;

namespace ArquitecturaBase.Api.Endpoints.Account;

/// <summary>
/// Qué medios de ingreso ofrece la pantalla de login (sección 10 del spec del ingreso con WhatsApp). Es anónimo y sin
/// límite propio, como el resto de las lecturas de /account: solo dice qué está configurado.
/// </summary>
internal sealed class LoginMethodsEndpoint : IEndpoint
{
    public void MapEndpoint(IEndpointRouteBuilder app) =>
        app.MapGet("/account/login-methods", async (
                IQueryHandler<GetLoginMethodsQuery, LoginMethodsResponse> handler,
                CancellationToken cancellationToken) =>
            (await handler.Handle(new GetLoginMethodsQuery(), cancellationToken)).ToHttpResult())
            .AllowAnonymous()
            .WithTags("Account");
}
