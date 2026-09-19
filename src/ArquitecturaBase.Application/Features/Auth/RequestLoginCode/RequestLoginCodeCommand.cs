using ArquitecturaBase.Application.Abstractions.Messaging;

namespace ArquitecturaBase.Application.Features.Auth.RequestLoginCode;

public sealed record RequestLoginCodeCommand(string? Email) : ICommand<RequestLoginCodeResponse>;
