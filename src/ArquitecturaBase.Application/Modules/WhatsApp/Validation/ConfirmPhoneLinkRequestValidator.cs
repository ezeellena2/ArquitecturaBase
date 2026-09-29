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
    }
}
