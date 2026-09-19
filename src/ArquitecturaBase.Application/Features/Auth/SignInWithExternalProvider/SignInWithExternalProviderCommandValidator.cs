using ArquitecturaBase.Application.Common.Validation;
using ArquitecturaBase.Application.Resources;
using FluentValidation;

namespace ArquitecturaBase.Application.Features.Auth.SignInWithExternalProvider;

internal sealed class SignInWithExternalProviderCommandValidator : AbstractValidator<SignInWithExternalProviderCommand>
{
    public SignInWithExternalProviderCommandValidator() =>
        RuleFor(command => command.ReturnUrl)
            .Cascade(CascadeMode.Stop)
            .Required()
            .Must(ReturnUrls.IsAuthorizeRequest)
            .WithMessage(_ => ValidationMessages.ReturnUrlInvalid);
}
