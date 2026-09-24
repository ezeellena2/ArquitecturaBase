using ArquitecturaBase.Application.Models.Users;

namespace ArquitecturaBase.Application.Validation.Users;

internal sealed class ListUsersRequestValidator() : UserListRequestValidator<ListUsersRequest>(ListUsersRequest.SortableFields);
