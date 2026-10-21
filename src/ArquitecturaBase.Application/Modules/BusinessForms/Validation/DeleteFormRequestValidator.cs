using ArquitecturaBase.Application.Modules.BusinessForms.Models;
using ArquitecturaBase.Application.Resources;
using ArquitecturaBase.Domain.Modules.BusinessForms;
using FluentValidation;

namespace ArquitecturaBase.Application.Modules.BusinessForms.Validation;

internal sealed class DeleteFormRequestValidator : AbstractValidator<DeleteFormRequest>
{
    public DeleteFormRequestValidator() => RuleFor(request => request.ExpectedRevision).InclusiveBetween(1, 9007199254740991)
        .WithMessage(_ => ErrorMessages.Get(FormDefinitionErrors.ContentInvalid.Code));
}
