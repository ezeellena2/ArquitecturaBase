using ArquitecturaBase.Application.Abstractions.Messaging;

namespace ArquitecturaBase.Application.Features.Users.UpdateUser;

/// <summary>Nombre y roles de un usuario. Los roles reemplazan a los que tenía, no se suman.</summary>
public sealed record UpdateUserCommand(Guid UserId, string? DisplayName, IReadOnlyCollection<string>? Roles) : ICommand;
