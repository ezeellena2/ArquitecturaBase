using ArquitecturaBase.Application.Common.Validation;
using ArquitecturaBase.Application.Modules.BusinessForms.Models;
using FluentValidation;
namespace ArquitecturaBase.Application.Modules.BusinessForms.Validation;
internal sealed class SaveFormRulesRequestValidator : AbstractValidator<SaveFormRulesRequest>
{
    public SaveFormRulesRequestValidator()
    {
        RuleFor(request => request.ExpectedRevision).NotNull().InclusiveBetween(0,9007199254740990);
        RuleFor(request => request.Rules).NotNull().Must(rules => rules is { Count: <= 20 });
    }
}
internal sealed class ListFormRuleRevisionsRequestValidator() : PagedRequestValidator<ListFormRuleRevisionsRequest>(ListFormRuleRevisionsRequest.SortableFields);
