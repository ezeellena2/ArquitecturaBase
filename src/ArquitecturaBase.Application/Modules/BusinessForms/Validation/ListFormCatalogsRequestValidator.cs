using ArquitecturaBase.Application.Common.Validation;
using ArquitecturaBase.Application.Modules.BusinessForms.Models;
using ArquitecturaBase.Application.Resources;
using ArquitecturaBase.Domain.Modules.BusinessForms;
using FluentValidation;

namespace ArquitecturaBase.Application.Modules.BusinessForms.Validation;

internal sealed class ListFormCatalogsRequestValidator : PagedRequestValidator<ListFormCatalogsRequest>
{
    public ListFormCatalogsRequestValidator() : base(ListFormCatalogsRequest.SortableFields)
    {
        RuleFor(request => request.Kind).Must(value => value is null or "Manual" or "Filters")
            .WithMessage(_ => ErrorMessages.Get(FormCatalogErrors.ContentInvalid.Code));
        RuleFor(request => request.State)
            .Must(value => value is null or "Active" or "Inactive" or "Unavailable")
            .WithMessage(_ => ValidationMessages.FormCatalogStateInvalid);
    }
}

internal sealed class ListFormCatalogItemsRequestValidator : PagedRequestValidator<ListFormCatalogItemsRequest>
{
    public ListFormCatalogItemsRequestValidator() : base(ListFormCatalogItemsRequest.SortableFields) =>
        RuleFor(request => request.Ids).Must(ids => ids is { Count: <= 100 } && !ids.Contains(Guid.Empty))
            .WithMessage(_ => FormCatalogRequestValidation.InvalidMessage());
}
