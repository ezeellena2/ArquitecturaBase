using ArquitecturaBase.Application.Models.Identity;

namespace ArquitecturaBase.Application.Models.Users;

public sealed record ListUsersRequest : UserListRequest
{
    public static readonly IReadOnlyCollection<string> SortableFields = ["email", "displayName", "createdAtUtc"];
}
