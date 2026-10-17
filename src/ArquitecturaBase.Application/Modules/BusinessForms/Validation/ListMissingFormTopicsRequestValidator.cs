using ArquitecturaBase.Application.Common.Validation;
using ArquitecturaBase.Application.Modules.BusinessForms.Models;
using ArquitecturaBase.Application.Resources;
using ArquitecturaBase.Domain.Modules.BusinessForms;
using FluentValidation;

namespace ArquitecturaBase.Application.Modules.BusinessForms.Validation;

internal sealed class ListMissingFormTopicsRequestValidator : PagedRequestValidator<ListMissingFormTopicsRequest>
{
    public ListMissingFormTopicsRequestValidator() : base(ListMissingFormTopicsRequest.SortableFields)
    {
        RuleFor(request => request.State).Must(value => value is null or "Pending" or "Discarded")
            .WithMessage(_ => ErrorMessages.Get(FormMissingRequestErrors.ContentInvalid.Code));
    }
}
