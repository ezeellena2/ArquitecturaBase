using ArquitecturaBase.Application.Common.Validation;
using ArquitecturaBase.Application.Modules.BusinessForms.Models;
using ArquitecturaBase.Application.Resources;
using ArquitecturaBase.Domain.Modules.BusinessForms;
using FluentValidation;

namespace ArquitecturaBase.Application.Modules.BusinessForms.Validation;

internal sealed class ListFormEntryOptionsRequestValidator : PagedRequestValidator<ListFormEntryOptionsRequest>
{
    public ListFormEntryOptionsRequestValidator() : base(ListFormEntryOptionsRequest.SortableFields)
    {
        RuleFor(request => request.PageSize).LessThanOrEqualTo(ListFormEntryOptionsRequest.MaximumPageSize)
            .WithMessage(_ => ValidationMessages.PageSizeInvalid);
        RuleFor(request => request.Answers).Must(answers => answers is null || answers.Count <= 40
                && answers.All(answer => answer.Key != Guid.Empty && answer.Value != Guid.Empty))
            .WithMessage(_ => ErrorMessages.Get(FormRecordErrors.InvalidResponse.Code));
    }
}

/// <summary>The shape of a send; the published version decides types, required answers and file limits.</summary>
internal sealed class SubmitFormEntryRequestValidator : AbstractValidator<SubmitFormEntryRequest>
{
    public SubmitFormEntryRequestValidator()
    {
        RuleFor(request => request.RequestId).NotEqual(Guid.Empty).WithMessage(_ => ValidationMessages.Required);
        RuleFor(request => request.VersionId).NotEqual(Guid.Empty).WithMessage(_ => ValidationMessages.Required);
        RuleFor(request => request.Answers).NotNull().WithMessage(_ => ErrorMessages.Get(FormRecordErrors.InvalidResponse.Code))
            .Must(answers => answers is null || answers.Count <= 40 && answers.All(answer => answer is not null && answer.FieldId != Guid.Empty
                && (answer.Value is null || answer.Value.Length <= 10_000)
                && (answer.ChoiceIds is null || answer.ChoiceIds.Count <= 20 && answer.ChoiceIds.All(choice => choice != Guid.Empty)))
                && answers.Select(answer => answer.FieldId).Distinct().Count() == answers.Count)
            .WithMessage(_ => ErrorMessages.Get(FormRecordErrors.InvalidResponse.Code));
        RuleFor(request => request.Files).NotNull().Must(files => files is null || files.Count <= 10 && files.All(file => file is not null
                && file.FieldId != Guid.Empty && file.Content is { Length: > 0 } && !string.IsNullOrWhiteSpace(file.Name)
                && !string.IsNullOrWhiteSpace(file.ContentType)))
            .WithMessage(_ => ErrorMessages.Get(FormRecordErrors.InvalidResponse.Code));
    }
}
