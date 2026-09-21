namespace ArquitecturaBase.Application.Features.Users.GetUsers;

internal sealed class GetUsersQueryValidator() : UserListRequestValidator<GetUsersQuery>(GetUsersQuery.SortableFields);
