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

        // Exactamente uno de los dos. Sin número, el correo es obligatorio, como siempre. El formato del número lo
        // controla el caso de uso: llega en formato internacional, el que devolvió el pedido del código.
        When(command => command.IsByPhone, () =>
                RuleFor(command => command.Phone)
                    .Must((command, _) => string.IsNullOrWhiteSpace(command.Email))
                    .WithMessage(_ => ValidationMessages.EmailOrPhone))
            .Otherwise(() => RuleFor(command => command.Email).ValidEmail());

        RuleFor(command => command.Code)
            .Cascade(CascadeMode.Stop)
            .Required()
            .Must(code => code!.Length == length && code.All(char.IsAsciiDigit))
            .WithMessage(command => command.IsByPhone ? ValidationMessages.LoginCodeFormatWhatsApp : ValidationMessages.LoginCodeFormat);

        RuleFor(command => command.ReturnUrl)
            .Cascade(CascadeMode.Stop)
            .Required()
            .Must(ReturnUrls.IsAuthorizeRequest)
            .WithMessage(_ => ValidationMessages.ReturnUrlInvalid);
    }
}
