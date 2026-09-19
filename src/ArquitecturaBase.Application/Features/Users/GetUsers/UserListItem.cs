namespace ArquitecturaBase.Application.Features.Users.GetUsers;

public sealed record UserListItem(Guid Id, string Email, string? DisplayName, bool IsActive, DateTime CreatedAtUtc);
