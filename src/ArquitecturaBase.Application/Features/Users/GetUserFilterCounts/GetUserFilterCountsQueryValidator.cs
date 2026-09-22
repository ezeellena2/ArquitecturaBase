using ArquitecturaBase.Application.Features.Users.GetUsers;

namespace ArquitecturaBase.Application.Features.Users.GetUserFilterCounts;

/// El mismo validador que el listado, con su misma lista blanca: estos números describen a ese listado, y si
/// uno aceptara un filtro que el otro rechaza, dejarían de hablar de lo mismo.
internal sealed class GetUserFilterCountsQueryValidator()
    : UserListRequestValidator<GetUserFilterCountsQuery>(GetUsersQuery.SortableFields);
