using ArquitecturaBase.Application.Common.Validation;
using ArquitecturaBase.Application.Modules.WhatsApp.Models;
using ArquitecturaBase.Application.Resources;
using FluentValidation;

namespace ArquitecturaBase.Application.Modules.WhatsApp.Validation;

internal sealed class ChangeWhatsAppModeRequestValidator : AbstractValidator<ChangeWhatsAppModeRequest>
{
    public ChangeWhatsAppModeRequestValidator()
    {
        RuleFor(request => request.ContactId).Required();
        RuleFor(request => request.Mode).IsInEnum().WithMessage(_ => ValidationMessages.Required);
        RuleFor(request => request.ExpectedRevision).GreaterThanOrEqualTo(0).WithMessage(_ => ValidationMessages.Required);
    }
}
