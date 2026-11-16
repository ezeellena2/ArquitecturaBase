using ArquitecturaBase.Application.Common.Validation;
using ArquitecturaBase.Application.Configuration.Auth;
using ArquitecturaBase.Application.Modules.WhatsApp.Models;
using ArquitecturaBase.Application.Resources;
using FluentValidation;
using Microsoft.Extensions.Options;

namespace ArquitecturaBase.Application.Modules.WhatsApp.Validation;

internal sealed class ConfirmPhoneLinkRequestValidator : AbstractValidator<ConfirmPhoneLinkRequest>
{
    public ConfirmPhoneLinkRequestValidator(IOptions<LoginCodeOptions> options)
    {
        ArgumentNullException.ThrowIfNull(options);

        RuleFor(request => request.Phone).Required();
        RuleFor(request => request.Code).ValidLoginCode(options.Value.Length, _ => ValidationMessages.LoginCodeFormatWhatsApp);

        // Sólo hace falta con un correo verificado, y eso lo decide el caso de uso.
        RuleFor(request => request.EmailCode)
            .ValidLoginCode(options.Value.Length, _ => ValidationMessages.LoginCodeFormat)
            .When(request => request.EmailCode is not null);
    }
}
