using ArquitecturaBase.Application.Common.Validation;
using ArquitecturaBase.Application.Modules.BusinessForms.Models;
using ArquitecturaBase.Application.Resources;
using ArquitecturaBase.Domain.Modules.BusinessForms;
using FluentValidation;
namespace ArquitecturaBase.Application.Modules.BusinessForms.Validation;
internal sealed class SaveFormSuggestionsRequestValidator : AbstractValidator<SaveFormSuggestionsRequest>
{
    public SaveFormSuggestionsRequestValidator()
    {
        RuleFor(request => request.ExpectedRevision).NotNull().WithMessage(_ => ErrorMessages.Get(FormSuggestionErrors.ContentInvalid.Code))
            .InclusiveBetween(0, 9007199254740990).WithMessage(_ => ErrorMessages.Get(FormSuggestionErrors.ContentInvalid.Code));
        RuleFor(request => request.Suggestions).NotNull().WithMessage(_ => ErrorMessages.Get(FormSuggestionErrors.ContentInvalid.Code))
            .Must(suggestions => suggestions is { Count: <= 20 }).WithMessage(_ => ErrorMessages.Get(FormSuggestionErrors.ContentInvalid.Code));
    }
}
internal sealed class ListFormSuggestionRevisionsRequestValidator() : PagedRequestValidator<ListFormSuggestionRevisionsRequest>(ListFormSuggestionRevisionsRequest.SortableFields);
