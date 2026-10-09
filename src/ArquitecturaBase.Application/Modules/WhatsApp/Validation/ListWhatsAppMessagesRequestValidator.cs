using ArquitecturaBase.Application.Common.Validation;
using ArquitecturaBase.Application.Modules.WhatsApp.Models;
using ArquitecturaBase.Application.Resources;
using FluentValidation;

namespace ArquitecturaBase.Application.Modules.WhatsApp.Validation;

internal sealed class ListWhatsAppMessagesRequestValidator : AbstractValidator<ListWhatsAppMessagesRequest>
{
    public ListWhatsAppMessagesRequestValidator()
    {
        RuleFor(request => request.ContactId).Required();
        RuleFor(request => request.Limit).InclusiveBetween(1, 100).WithMessage(_ => ValidationMessages.Required);
        RuleFor(request => request.BeforeId).Must((request, id) => request.BeforeAtUtc.HasValue == id.HasValue
            && id != Guid.Empty).WithMessage(_ => ValidationMessages.Required);
    }
}
