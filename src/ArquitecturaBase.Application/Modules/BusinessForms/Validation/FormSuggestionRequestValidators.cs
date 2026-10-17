using ArquitecturaBase.Application.Common.Validation;
using ArquitecturaBase.Application.Modules.BusinessForms.Models;
using FluentValidation;
namespace ArquitecturaBase.Application.Modules.BusinessForms.Validation;
internal sealed class SaveFormSuggestionsRequestValidator : AbstractValidator<SaveFormSuggestionsRequest>
{
    public SaveFormSuggestionsRequestValidator()
    {
        RuleFor(request => request.ExpectedRevision).NotNull().InclusiveBetween(0,9007199254740990);
        RuleFor(request => request.Suggestions).NotNull().Must(suggestions => suggestions is { Count: <= 20 });
    }
}
internal sealed class ListFormSuggestionRevisionsRequestValidator() : PagedRequestValidator<ListFormSuggestionRevisionsRequest>(ListFormSuggestionRevisionsRequest.SortableFields);
