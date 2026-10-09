using ArquitecturaBase.Application.Common.Validation;
using ArquitecturaBase.Application.Modules.WhatsApp.Models;
using ArquitecturaBase.Application.Resources;
using FluentValidation;

namespace ArquitecturaBase.Application.Modules.WhatsApp.Validation;

internal sealed class MarkWhatsAppChatReadRequestValidator : AbstractValidator<MarkWhatsAppChatReadRequest>
{
    public MarkWhatsAppChatReadRequestValidator()
    {
        RuleFor(request => request.ContactId).Required();
        RuleFor(request => request.ThroughSequence).GreaterThanOrEqualTo(0).WithMessage(_ => ValidationMessages.Required);
    }
}
