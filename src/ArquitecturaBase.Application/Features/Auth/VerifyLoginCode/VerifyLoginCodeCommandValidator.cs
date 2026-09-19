using ArquitecturaBase.Application.Common.Validation;
using ArquitecturaBase.Application.Resources;
using FluentValidation;
using Microsoft.Extensions.Options;

namespace ArquitecturaBase.Application.Features.Auth.VerifyLoginCode;

internal sealed class VerifyLoginCodeCommandValidator : AbstractValidator<VerifyLoginCodeCommand>
{
    public VerifyLoginCodeCommandValidator(IOptions<LoginCodeOptions> options)
    {
        ArgumentNullException.ThrowIfNull(options);
        var length = options.Value.Length;

        RuleFor(command => command.Email).ValidEmail();

        RuleFor(command => command.Code)
            .Cascade(CascadeMode.Stop)
            .Required()
            .Must(code => code!.Length == length && code.All(char.IsAsciiDigit))
            .WithMessage(_ => ValidationMessages.LoginCodeFormat);

        RuleFor(command => command.ReturnUrl)
            .Cascade(CascadeMode.Stop)
            .Required()
            .Must(ReturnUrls.IsAuthorizeRequest)
            .WithMessage(_ => ValidationMessages.ReturnUrlInvalid);
    }
}
