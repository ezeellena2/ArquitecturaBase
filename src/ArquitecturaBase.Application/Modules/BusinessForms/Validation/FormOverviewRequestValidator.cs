using ArquitecturaBase.Application.Common.Validation;
using ArquitecturaBase.Application.Modules.BusinessForms.Models;
using ArquitecturaBase.Application.Resources;
using ArquitecturaBase.Domain.Modules.BusinessForms;
using FluentValidation;

namespace ArquitecturaBase.Application.Modules.BusinessForms.Validation;

internal sealed class ListFormOverviewRequestValidator : PagedRequestValidator<ListFormOverviewRequest>
{
    public ListFormOverviewRequestValidator() : base(ListFormOverviewRequest.SortableFields)
    {
        RuleFor(request => request.State).Must(state => state is "Active" or "Deactivated")
            .WithMessage(_ => ErrorMessages.Get(FormRecordErrors.FilterInvalid.Code));
    }
}