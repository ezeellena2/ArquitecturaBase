using ArquitecturaBase.Application.Abstractions.Messaging;

namespace ArquitecturaBase.Application.Features.Auth.SignInWithExternalProvider;

public sealed record SignInWithExternalProviderCommand(string? ReturnUrl)
    : ICommand<SignInWithExternalProviderResponse>, IPersistChangesOnFailure;
