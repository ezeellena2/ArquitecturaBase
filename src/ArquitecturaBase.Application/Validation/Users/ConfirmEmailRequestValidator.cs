using ArquitecturaBase.Application.Configuration.Auth;
using ArquitecturaBase.Application.Common.Validation;
using ArquitecturaBase.Application.Models.Users;
using ArquitecturaBase.Application.Resources;
using FluentValidation;
using Microsoft.Extensions.Options;

namespace ArquitecturaBase.Application.Validation.Users;

internal sealed class ConfirmEmailRequestValidator : AbstractValidator<ConfirmEmailRequest>
{
    public ConfirmEmailRequestValidator(IOptions<LoginCodeOptions> options)
    {
        ArgumentNullException.ThrowIfNull(options);

        RuleFor(request => request.Email).ValidEmail();
        RuleFor(request => request.Code).ValidLoginCode(options.Value.Length, _ => ValidationMessages.LoginCodeFormat);

        // Sólo hace falta con un número verificado, y eso lo decide el caso de uso (N11).
        RuleFor(request => request.PhoneCode)
            .ValidLoginCode(options.Value.Length, _ => ValidationMessages.LoginCodeFormatWhatsApp)
            .When(request => request.PhoneCode is not null);
    }
}
