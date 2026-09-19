using ArquitecturaBase.Api.Endpoints;
using ArquitecturaBase.Api.ErrorHandling;
using ArquitecturaBase.Api.IntegrationTests.TestFeatures.Widgets;
using ArquitecturaBase.Application.Abstractions.Messaging;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace ArquitecturaBase.Api.IntegrationTests.TestFeatures;

/// <summary>Endpoints que existen solo en los tests.</summary>
internal sealed class TestEndpoints : IEndpoint
{
    public void MapEndpoint(IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/test");

        group.MapPost("/widgets", async (
                CreateWidgetCommand command,
                ICommandHandler<CreateWidgetCommand, Guid> handler,
                CancellationToken cancellationToken) =>
            (await handler.Handle(command, cancellationToken)).ToHttpResult());

        group.MapGet("/widgets/{id:guid}", async (
                Guid id,
                IQueryHandler<GetWidgetByIdQuery, WidgetDetailsResponse> handler,
                CancellationToken cancellationToken) =>
            (await handler.Handle(new GetWidgetByIdQuery(id), cancellationToken)).ToHttpResult());

        group.MapGet("/boom", IResult () =>
            throw new InvalidOperationException("Sensitive detail that must never reach the client."));

        group.MapPost("/dates", (DateEchoRequest request) => TypedResults.Ok(request));
    }
}
