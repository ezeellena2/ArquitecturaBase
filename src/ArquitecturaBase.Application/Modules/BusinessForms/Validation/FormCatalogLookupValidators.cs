using ArquitecturaBase.Application.Modules.BusinessForms.Models;
using ArquitecturaBase.Application.Resources;
using FluentValidation;

namespace ArquitecturaBase.Application.Modules.BusinessForms.Validation;

internal sealed class LookupFormCatalogPartyRequestValidator : AbstractValidator<LookupFormCatalogPartyRequest>
{
    public LookupFormCatalogPartyRequestValidator() => RuleFor(request => request.TaxId)
        .Must(taxId => !string.IsNullOrWhiteSpace(taxId)).WithMessage(_ => ValidationMessages.Required)
        .MaximumLength(LookupFormCatalogPartyRequest.MaximumTaxIdLength).WithMessage(_ => ValidationMessages.MaxLength);
}
