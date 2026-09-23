using ArquitecturaBase.Api.Endpoints;
using ArquitecturaBase.Api.ErrorHandling;
using ArquitecturaBase.Api.IntegrationTests.TestFeatures.LoginLinks;
using ArquitecturaBase.Application.Abstractions.Messaging;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Routing;

namespace ArquitecturaBase.Api.IntegrationTests.TestFeatures;

/// <summary>
/// POST /test/login-links con <c>{ userId }</c>: devuelve la URL del enlace, con el token en el fragmento, como la
/// mandaría el bot al chat.
/// </summary>
internal sealed class LoginLinkTestEndpoints : IEndpoint
{
    public void MapEndpoint(IEndpointRouteBuilder app) =>
        app.MapPost("/test/login-links", async (
                IssueLoginLinkCommand command,
                ICommandHandler<IssueLoginLinkCommand, IssueLoginLinkResponse> handler,
                CancellationToken cancellationToken) =>
            (await handler.Handle(command, cancellationToken)).ToHttpResult());
}
