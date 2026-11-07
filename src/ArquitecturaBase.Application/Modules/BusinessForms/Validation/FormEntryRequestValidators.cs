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
        RuleFor(request => request.Answers).Must(FormEntrySendRules.ValidChosen)
            .WithMessage(_ => ErrorMessages.Get(FormRecordErrors.InvalidResponse.Code));
        // LD1-A: one value chosen first, as long as a fixed filter value, or the options without one; not both.
        RuleFor(request => request.Group).Must((request, group) => group is null || !request.WithoutGroup && group.Length is > 0 and <= 300
                && ArquitecturaBase.Domain.Common.StorableText.IsStorable(group))
            .WithMessage(_ => ErrorMessages.Get(FormRecordErrors.InvalidResponse.Code));
    }
}

internal sealed class ListFormEntryGroupsRequestValidator : AbstractValidator<ListFormEntryGroupsRequest>
{
    public ListFormEntryGroupsRequestValidator() => RuleFor(request => request.Answers).Must(FormEntrySendRules.ValidChosen)
        .WithMessage(_ => ErrorMessages.Get(FormRecordErrors.InvalidResponse.Code));
}

/// <summary>The shape of a send; the published version decides types, required answers and file limits.</summary>
internal sealed class SubmitFormEntryRequestValidator : AbstractValidator<SubmitFormEntryRequest>
{
    public SubmitFormEntryRequestValidator()
    {
        RuleFor(request => request.RequestId).NotEqual(Guid.Empty).WithMessage(_ => ValidationMessages.Required);
        RuleFor(request => request.VersionId).NotEqual(Guid.Empty).WithMessage(_ => ValidationMessages.Required);
        RuleFor(request => request.Answers).NotNull().WithMessage(_ => ErrorMessages.Get(FormRecordErrors.InvalidResponse.Code))
            .Must(FormEntrySendRules.ValidAnswers).WithMessage(_ => ErrorMessages.Get(FormRecordErrors.InvalidResponse.Code));
        RuleFor(request => request.Files).NotNull().Must(FormEntrySendRules.ValidFiles)
            .WithMessage(_ => ErrorMessages.Get(FormRecordErrors.InvalidResponse.Code));
    }
}

/// <summary>A correction is a send plus why and the files of the replaced version that stay, up to ten files in all.</summary>
internal sealed class CorrectFormEntryRequestValidator : AbstractValidator<CorrectFormEntryRequest>
{
    public CorrectFormEntryRequestValidator()
    {
        RuleFor(request => request.RequestId).NotEqual(Guid.Empty).WithMessage(_ => ValidationMessages.Required);
        RuleFor(request => request.VersionId).NotEqual(Guid.Empty).WithMessage(_ => ValidationMessages.Required);
        RuleFor(request => request.Answers).NotNull().WithMessage(_ => ErrorMessages.Get(FormRecordErrors.InvalidResponse.Code))
            .Must(FormEntrySendRules.ValidAnswers).WithMessage(_ => ErrorMessages.Get(FormRecordErrors.InvalidResponse.Code));
        RuleFor(request => request.Files).NotNull().Must(FormEntrySendRules.ValidFiles)
            .WithMessage(_ => ErrorMessages.Get(FormRecordErrors.InvalidResponse.Code));
        RuleFor(request => request.KeptFileIds).NotNull().Must((request, kept) => kept is null || kept.All(id => id != Guid.Empty)
                && kept.Distinct().Count() == kept.Count && kept.Count + (request.Files?.Count ?? 0) <= 10)
            .WithMessage(_ => ErrorMessages.Get(FormRecordErrors.InvalidResponse.Code));
        FormEntrySendRules.Reason(RuleFor(request => request.Reason));
    }
}

internal sealed class VoidFormRecordRequestValidator : AbstractValidator<VoidFormRecordRequest>
{
    public VoidFormRecordRequestValidator() => FormEntrySendRules.Reason(RuleFor(request => request.Reason));
}

/// <summary>The shape shared by a send and a correction, and the reason of a correction or a void.</summary>
internal static class FormEntrySendRules
{
    /// <summary>The option chosen in each question a dependent list waits for: up to 40, without empty identities.</summary>
    public static bool ValidChosen(IReadOnlyDictionary<Guid, Guid>? answers) =>
        answers is null || answers.Count <= 40 && answers.All(answer => answer.Key != Guid.Empty && answer.Value != Guid.Empty);

    public static bool ValidAnswers(IReadOnlyList<FormEntryAnswer>? answers) =>
        answers is null || answers.Count <= 40 && answers.All(answer => ValidAnswer(answer, false))
            && answers.Select(answer => answer.FieldId).Distinct().Count() == answers.Count;

    // The block takes up to 100 items of up to 40 answers, each question once; an answer of an item is only a value.
    private static bool ValidAnswer(FormEntryAnswer? answer, bool inItem) => answer is not null && answer.FieldId != Guid.Empty
        && (answer.Value is null || answer.Value.Length <= 10_000)
        && (answer.ChoiceIds is null || !inItem && answer.ChoiceIds.Count <= 20 && answer.ChoiceIds.All(choice => choice != Guid.Empty))
        && (answer.Rows is null || !inItem && answer.Value is null && answer.ChoiceIds is null && answer.Rows.Count <= BusinessFormRecordValue.MaxRows
            && answer.Rows.All(item => item is not null && item.Count <= 40 && item.All(inner => ValidAnswer(inner, true))
                && item.Select(inner => inner.FieldId).Distinct().Count() == item.Count));

    public static bool ValidFiles(IReadOnlyList<FormEntryFileInput>? files) =>
        files is null || files.Count <= 10 && files.All(file => file is not null
            && file.FieldId != Guid.Empty && file.Content is { Length: > 0 } && !string.IsNullOrWhiteSpace(file.Name)
            && !string.IsNullOrWhiteSpace(file.ContentType));

    public static void Reason<T>(IRuleBuilderInitial<T, string?> rule) => rule
        .Must(reason => BusinessFormRecord.NormalizeReason(reason).Error != FormRecordErrors.ReasonRequired)
        .WithMessage(_ => ErrorMessages.Get(FormRecordErrors.ReasonRequired.Code))
        .Must(reason => BusinessFormRecord.NormalizeReason(reason).Error != FormRecordErrors.ReasonInvalid)
        .WithMessage(_ => ErrorMessages.Get(FormRecordErrors.ReasonInvalid.Code));
}
