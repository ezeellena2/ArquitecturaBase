using ArquitecturaBase.Application.Common.Validation;
using ArquitecturaBase.Application.Modules.WhatsApp.Models;

namespace ArquitecturaBase.Application.Modules.WhatsApp.Validation;

internal sealed class ListWhatsAppChatsRequestValidator : PagedRequestValidator<ListWhatsAppChatsRequest>
{
    public ListWhatsAppChatsRequestValidator() : base(ListWhatsAppChatsRequest.SortableFields) { }
}
