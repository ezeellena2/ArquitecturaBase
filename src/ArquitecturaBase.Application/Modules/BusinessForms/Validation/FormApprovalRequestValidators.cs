using ArquitecturaBase.Application.Common.Validation;
using ArquitecturaBase.Application.Modules.BusinessForms.Models;
using ArquitecturaBase.Application.Resources;
using ArquitecturaBase.Domain.Common;
using ArquitecturaBase.Domain.Modules.BusinessForms;
using FluentValidation;
namespace ArquitecturaBase.Application.Modules.BusinessForms.Validation;

internal sealed class ListFormApprovalsRequestValidator : PagedRequestValidator<ListFormApprovalsRequest>
{
    public ListFormApprovalsRequestValidator() : base(ListFormApprovalsRequest.SortableFields)
    { RuleFor(request => request.DefinitionId).Must(id => id is null || id != Guid.Empty); }
}
internal sealed class DecideFormApprovalRequestValidator : AbstractValidator<DecideFormApprovalRequest>
{
    public DecideFormApprovalRequestValidator()
    {
        RuleFor(request => request.ExpectedRevision).NotNull().InclusiveBetween(1, 9007199254740990);
        RuleFor(request => request.Decision).Must(decision => decision is "Approved" or "Rejected").WithMessage(_ => ErrorMessages.Get(FormApprovalErrors.ContentInvalid.Code));
        RuleFor(request => request.Reason).Must((request, reason) => request.Decision == "Approved" ? reason is null : !string.IsNullOrWhiteSpace(reason))
            .WithMessage(_ => ErrorMessages.Get(FormApprovalErrors.ReasonRequired.Code));
        RuleFor(request => request.Reason).Must(reason => reason is null || reason.Trim().Length <= 500 && StorableText.IsStorable(reason))
            .WithMessage(_ => ErrorMessages.Get(FormApprovalErrors.ContentInvalid.Code));
    }
}
