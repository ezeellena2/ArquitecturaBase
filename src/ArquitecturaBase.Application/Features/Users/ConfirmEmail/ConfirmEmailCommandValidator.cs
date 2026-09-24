using ArquitecturaBase.Application.Common.Validation;
using ArquitecturaBase.Application.Features.Auth;
using ArquitecturaBase.Application.Resources;
using FluentValidation;
using Microsoft.Extensions.Options;

namespace ArquitecturaBase.Application.Features.Users.ConfirmEmail;

internal sealed class ConfirmEmailCommandValidator : AbstractValidator<ConfirmEmailCommand>
{
    public ConfirmEmailCommandValidator(IOptions<LoginCodeOptions> options)
    {
        ArgumentNullException.ThrowIfNull(options);

        RuleFor(command => command.Email).ValidEmail();

        RuleFor(command => command.Code).ValidLoginCode(options.Value.Length, _ => ValidationMessages.LoginCodeFormat);
    }
}
