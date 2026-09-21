using ArquitecturaBase.Api.Authorization;
using ArquitecturaBase.Api.ErrorHandling;
using ArquitecturaBase.Application.Abstractions.Messaging;
using ArquitecturaBase.Application.Common.Pagination;
using ArquitecturaBase.Application.Features.Users.CreateUser;
using ArquitecturaBase.Application.Features.Users.GetUsers;
using ArquitecturaBase.Domain.Authorization;

namespace ArquitecturaBase.Api.Endpoints.Users;

/// <summary>
/// ABM de usuarios (sección 10 del spec de la Fase 4). El listado es el ejemplo del patrón de paginado
/// (sección 6.2): los parámetros se enlazan a mano porque [AsParameters] haría obligatorios los int de PagedRequest.
/// </summary>
internal sealed class UsersEndpoints : IEndpoint
{
    public void MapEndpoint(IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/users").WithTags("Users");

        group.MapGet("", async (
                int? page,
                int? pageSize,
                string? sort,
                string? search,
                IQueryHandler<GetUsersQuery, PagedResult<UserListItem>> handler,
                CancellationToken cancellationToken) =>
            (await handler.Handle(
                new GetUsersQuery
                {
                    Page = page ?? PagedRequest.DefaultPage,
                    PageSize = pageSize ?? PagedRequest.DefaultPageSize,
                    Sort = sort,
                    Search = search,
                },
                cancellationToken)).ToHttpResult())
            .RequirePermission(Permissions.Users.Read);

        group.MapPost("", async (
                CreateUserCommand command,
                ICommandHandler<CreateUserCommand, Guid> handler,
                CancellationToken cancellationToken) =>
            (await handler.Handle(command, cancellationToken)).ToHttpResult())
            .RequirePermission(Permissions.Users.Manage);
    }
}
