using ArquitecturaBase.Application.Abstractions.Messaging;

namespace ArquitecturaBase.Application.Features.Auth.VerifyLoginCode;

public sealed record VerifyLoginCodeCommand(string? Email, string? Code, string? ReturnUrl)
    : ICommand<VerifyLoginCodeResponse>, IPersistChangesOnFailure;
