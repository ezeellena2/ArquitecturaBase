using ArquitecturaBase.Application.Abstractions.Messaging;

namespace ArquitecturaBase.Application.Features.Users.GetCurrentUser;

public sealed record GetCurrentUserQuery : IQuery<CurrentUserResponse>;
