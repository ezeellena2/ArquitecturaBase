using ArquitecturaBase.Application.Common.Validation;
using ArquitecturaBase.Application.Modules.BusinessForms.Models;
using ArquitecturaBase.Application.Resources;
using ArquitecturaBase.Domain.Modules.BusinessForms;
using FluentValidation;
namespace ArquitecturaBase.Application.Modules.BusinessForms.Validation;
internal sealed class FormNavigationRequestValidator() : PagedRequestValidator<FormNavigationRequest>(FormNavigationRequest.SortableFields);
internal sealed class ListFormRecordsRequestValidator : PagedRequestValidator<ListFormRecordsRequest>
{
    public ListFormRecordsRequestValidator() : base(ListFormRecordsRequest.SortableFields)
    {
        RuleFor(request => request.Conditions).NotNull().Must(value => value is { Count: <= 100 } && value.All(condition => condition is not null && condition.Values is null or { Count: <= 200 } && (condition.Values?.All(item => item is not null && item.Length <= 600) ?? true)));
        RuleFor(request => request.VersionIds).NotNull().Must(value => value is { Count: <= 100 });
        RuleFor(request => request.State).Must(value => value is null or "Received" or "PendingApproval" or "Approved" or "Rejected").WithMessage(_ => ErrorMessages.Get(FormRecordErrors.FilterInvalid.Code));
        RuleFor(request => request.ActorIds).NotNull().Must(value => value is { Count: <= 100 });
        RuleFor(request => request.ReceivedFromUtc).Must(value => value is null || value.Value.Kind == DateTimeKind.Utc).WithMessage(_ => ErrorMessages.Get(FormRecordErrors.FilterInvalid.Code));
        RuleFor(request => request.ReceivedToUtc).Must(value => value is null || value.Value.Kind == DateTimeKind.Utc).WithMessage(_ => ErrorMessages.Get(FormRecordErrors.FilterInvalid.Code));
    }
}

internal sealed class FormRecordFacetRequestValidator : PagedRequestValidator<FormRecordFacetRequest>
{
    public FormRecordFacetRequestValidator() : base(FormRecordFacetRequest.SortableFields)
    {
        RuleFor(request => request.Filter).NotNull();
        RuleFor(request => request.Dimension).Must(value => value is "actor" or "version" or "choice" or "presence").WithMessage(_ => ErrorMessages.Get(FormRecordErrors.FilterInvalid.Code));
    }
}
