using ArquitecturaBase.Application.Common.Validation;
using ArquitecturaBase.Application.Features.Auth;
using ArquitecturaBase.Application.Resources;
using FluentValidation;
using Microsoft.Extensions.Options;

namespace ArquitecturaBase.Application.Features.Users.ConfirmPhoneLink;

/// <summary>
/// El número es obligatorio; si está en formato internacional lo controla el caso de uso, que responde
/// <c>Users.Phone.Invalid</c>, como el verify del ingreso.
/// </summary>
internal sealed class ConfirmPhoneLinkCommandValidator : AbstractValidator<ConfirmPhoneLinkCommand>
{
    public ConfirmPhoneLinkCommandValidator(IOptions<LoginCodeOptions> options)
    {
        ArgumentNullException.ThrowIfNull(options);

        RuleFor(command => command.Phone).Required();

        RuleFor(command => command.Code).ValidLoginCode(options.Value.Length, _ => ValidationMessages.LoginCodeFormatWhatsApp);
    }
}
