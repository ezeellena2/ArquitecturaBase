using ArquitecturaBase.Application.Common.Validation;

namespace ArquitecturaBase.Application.Features.Users.GetUsers;

internal sealed class GetUsersQueryValidator() : PagedRequestValidator<GetUsersQuery>(GetUsersQuery.SortableFields);
