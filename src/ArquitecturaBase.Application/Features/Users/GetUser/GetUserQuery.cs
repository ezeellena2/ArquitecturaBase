using ArquitecturaBase.Application.Abstractions.Messaging;

namespace ArquitecturaBase.Application.Features.Users.GetUser;

public sealed record GetUserQuery(Guid UserId) : IQuery<UserDetail>;
