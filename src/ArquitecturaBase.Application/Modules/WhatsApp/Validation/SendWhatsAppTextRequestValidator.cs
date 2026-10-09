using ArquitecturaBase.Application.Common.Validation;
using ArquitecturaBase.Application.Modules.WhatsApp.Models;
using ArquitecturaBase.Application.Resources;
using ArquitecturaBase.Domain.Modules.WhatsApp;
using FluentValidation;

namespace ArquitecturaBase.Application.Modules.WhatsApp.Validation;

internal sealed class SendWhatsAppTextRequestValidator : AbstractValidator<SendWhatsAppTextRequest>
{
    public SendWhatsAppTextRequestValidator()
    {
        RuleFor(request => request.ContactId).Required();
        RuleFor(request => request.RequestId).Required();
        RuleFor(request => request.Body).Cascade(CascadeMode.Stop).Required()
            .MaxLength(WhatsAppMessage.MaxBodyLength)
            .Must(body => body is not null && WhatsAppOutgoingMessage.IsStorableBody(body)).WithMessage(_ => ValidationMessages.Required);
    }
}
