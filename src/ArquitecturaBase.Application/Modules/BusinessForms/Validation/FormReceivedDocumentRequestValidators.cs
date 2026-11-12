using ArquitecturaBase.Application.Common.Validation;
using ArquitecturaBase.Application.Modules.BusinessForms.Models;
using ArquitecturaBase.Application.Resources;
using ArquitecturaBase.Domain.Modules.BusinessForms;
using FluentValidation;

namespace ArquitecturaBase.Application.Modules.BusinessForms.Validation;

// The inbox list (DOC5-A): its page and order, one of its states, and a received interval of UTC instants whose end comes after its start.
internal sealed class ListFormReceivedDocumentsRequestValidator : PagedRequestValidator<ListFormReceivedDocumentsRequest>
{
    public ListFormReceivedDocumentsRequestValidator() : base(ListFormReceivedDocumentsRequest.SortableFields)
    {
        RuleFor(request => request.State).Must(state => state is null || ListFormReceivedDocumentsRequest.States.Contains(state, StringComparer.Ordinal))
            .WithMessage(_ => ErrorMessages.Get(FormReceivedDocumentErrors.ContentInvalid.Code));
        RuleFor(request => request.ReceivedFromUtc).Must(value => value is null or { Kind: DateTimeKind.Utc })
            .WithMessage(_ => ErrorMessages.Get(FormReceivedDocumentErrors.ContentInvalid.Code));
        RuleFor(request => request.ReceivedToUtc).Must((request, value) => value is null || value.Value.Kind == DateTimeKind.Utc
                && (request.ReceivedFromUtc is null || request.ReceivedFromUtc < value))
            .WithMessage(_ => ErrorMessages.Get(FormReceivedDocumentErrors.ContentInvalid.Code));
        RuleFor(request => request.DefinitionId).Must(value => value != Guid.Empty).WithMessage(_ => ErrorMessages.Get(FormReceivedDocumentErrors.ContentInvalid.Code));
        RuleFor(request => request.SenderUserId).Must(value => value != Guid.Empty).WithMessage(_ => ErrorMessages.Get(FormReceivedDocumentErrors.ContentInvalid.Code));
    }
}
