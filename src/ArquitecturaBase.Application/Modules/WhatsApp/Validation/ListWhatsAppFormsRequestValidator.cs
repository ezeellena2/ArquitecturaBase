using ArquitecturaBase.Application.Common.Validation;
using ArquitecturaBase.Application.Models.StructuredData;
using ArquitecturaBase.Application.Modules.WhatsApp.Models;
using FluentValidation;

namespace ArquitecturaBase.Application.Modules.WhatsApp.Validation;

internal sealed class ListWhatsAppFormsRequestValidator : PagedRequestValidator<ListWhatsAppFormsRequest>
{
    public ListWhatsAppFormsRequestValidator() : base(ListStructuredFormsRequest.SortableFields)
    {
        RuleFor(request => request.ContactId).NotEmpty();
    }
}
