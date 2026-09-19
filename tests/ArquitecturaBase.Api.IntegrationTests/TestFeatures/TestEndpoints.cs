using ArquitecturaBase.Api.Endpoints;
using ArquitecturaBase.Api.ErrorHandling;
using ArquitecturaBase.Api.IntegrationTests.TestFeatures.Widgets;
using ArquitecturaBase.Application.Abstractions.Messaging;
using ArquitecturaBase.Application.Common.Pagination;
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

        group.MapGet("/widgets", async (
                int? page,
                int? pageSize,
                string? sort,
                string? search,
                IQueryHandler<GetWidgetsQuery, PagedResult<WidgetResponse>> handler,
                CancellationToken cancellationToken) =>
            (await handler.Handle(
                new GetWidgetsQuery
                {
                    Page = page ?? PagedRequest.DefaultPage,
                    PageSize = pageSize ?? PagedRequest.DefaultPageSize,
                    Sort = sort,
                    Search = search,
                },
                cancellationToken)).ToHttpResult());

        group.MapGet("/boom", IResult () =>
            throw new InvalidOperationException("Sensitive detail that must never reach the client."));

        group.MapPost("/dates", (DateEchoRequest request) => TypedResults.Ok(request));

        // Errores que arma el framework, sin pasar por Result: 401/403 de la autorización y respuestas vacías
        // con cualquier código (simulan, por ejemplo, el 429 del rate limiter).
        group.MapGet("/protected", () => TypedResults.NoContent()).RequireAuthorization();
        group.MapGet("/admin", () => TypedResults.NoContent()).RequireAuthorization(policy => policy.RequireRole("Admin"));
        group.MapGet("/status/{statusCode:int}", (int statusCode) => TypedResults.StatusCode(statusCode));
    }
}
