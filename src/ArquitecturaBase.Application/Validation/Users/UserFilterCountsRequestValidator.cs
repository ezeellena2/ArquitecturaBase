using ArquitecturaBase.Application.Models.Users;

namespace ArquitecturaBase.Application.Validation.Users;

/// <summary>Conserva la misma lista blanca y los mismos filtros que el listado.</summary>
internal sealed class UserFilterCountsRequestValidator()
    : UserListRequestValidator<UserFilterCountsRequest>(ListUsersRequest.SortableFields);
