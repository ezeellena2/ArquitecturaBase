using ArquitecturaBase.Application.Common.Validation;
using ArquitecturaBase.Application.Modules.BusinessForms.Models;
using ArquitecturaBase.Application.Resources;
using ArquitecturaBase.Domain.Modules.BusinessForms;
using FluentValidation;
namespace ArquitecturaBase.Application.Modules.BusinessForms.Validation;
internal sealed class SaveFormRulesRequestValidator : AbstractValidator<SaveFormRulesRequest>
{
    public SaveFormRulesRequestValidator()
    {
        RuleFor(request => request.ExpectedRevision).NotNull().WithMessage(_ => ErrorMessages.Get(FormRuleErrors.ContentInvalid.Code))
            .InclusiveBetween(0, 9007199254740990).WithMessage(_ => ErrorMessages.Get(FormRuleErrors.ContentInvalid.Code));
        RuleFor(request => request.Rules).NotNull().WithMessage(_ => ErrorMessages.Get(FormRuleErrors.ContentInvalid.Code))
            .Must(rules => rules is { Count: <= 20 }).WithMessage(_ => ErrorMessages.Get(FormRuleErrors.ContentInvalid.Code));
    }
}
internal sealed class ListFormRuleRevisionsRequestValidator() : PagedRequestValidator<ListFormRuleRevisionsRequest>(ListFormRuleRevisionsRequest.SortableFields);
