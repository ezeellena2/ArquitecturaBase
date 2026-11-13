using ArquitecturaBase.Application.Common.Validation;
using ArquitecturaBase.Application.Modules.BusinessForms.Models;
using ArquitecturaBase.Application.Resources;
using ArquitecturaBase.Domain.Modules.BusinessForms;
using FluentValidation;
namespace ArquitecturaBase.Application.Modules.BusinessForms.Validation;
internal sealed class ListFormRecordsRequestValidator : PagedRequestValidator<ListFormRecordsRequest>
{
    public ListFormRecordsRequestValidator() : base(ListFormRecordsRequest.SortableFields)
    {
        RuleFor(request => request.Conditions).NotNull().WithMessage(_ => ErrorMessages.Get(FormRecordErrors.FilterInvalid.Code))
            .Must(value => value is { Count: <= 100 } && value.All(condition => condition is not null && condition.Values is null or { Count: <= 200 } && (condition.Values?.All(item => item is not null && item.Length <= 600) ?? true)))
            .WithMessage(_ => ErrorMessages.Get(FormRecordErrors.FilterInvalid.Code));
        RuleFor(request => request.VersionIds).NotNull().WithMessage(_ => ErrorMessages.Get(FormRecordErrors.FilterInvalid.Code))
            .Must(value => value is { Count: <= 100 }).WithMessage(_ => ErrorMessages.Get(FormRecordErrors.FilterInvalid.Code));
        RuleFor(request => request.State).Must(value => value is null or "Received" or "PendingApproval" or "Approved" or "Rejected" or "Voided").WithMessage(_ => ErrorMessages.Get(FormRecordErrors.FilterInvalid.Code));
        RuleFor(request => request.ActorIds).NotNull().WithMessage(_ => ErrorMessages.Get(FormRecordErrors.FilterInvalid.Code))
            .Must(value => value is { Count: <= 100 }).WithMessage(_ => ErrorMessages.Get(FormRecordErrors.FilterInvalid.Code));
        RuleFor(request => request.ReceivedFromUtc).Must(value => value is null || value.Value.Kind == DateTimeKind.Utc).WithMessage(_ => ErrorMessages.Get(FormRecordErrors.FilterInvalid.Code));
        RuleFor(request => request.ReceivedToUtc).Must(value => value is null || value.Value.Kind == DateTimeKind.Utc).WithMessage(_ => ErrorMessages.Get(FormRecordErrors.FilterInvalid.Code));
    }
}

internal sealed class FormRecordFacetRequestValidator : PagedRequestValidator<FormRecordFacetRequest>
{
    public FormRecordFacetRequestValidator() : base(FormRecordFacetRequest.SortableFields)
    {
        RuleFor(request => request.Filter).NotNull().WithMessage(_ => ErrorMessages.Get(FormRecordErrors.FilterInvalid.Code));
        RuleFor(request => request.Dimension).Must(value => value is "actor" or "version" or "choice" or "presence").WithMessage(_ => ErrorMessages.Get(FormRecordErrors.FilterInvalid.Code));
    }
}
