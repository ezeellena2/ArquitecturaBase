using ArquitecturaBase.Application.Modules.BusinessForms.Models;
using ArquitecturaBase.Application.Resources;
using ArquitecturaBase.Domain.Modules.BusinessForms;
using FluentValidation;

namespace ArquitecturaBase.Application.Modules.BusinessForms.Validation;

internal static class FormFolderRequestValidation
{
    public const int MaxFoldersPerItem = 100;
    public static readonly IReadOnlyList<string> Kinds = ["forms", "designs", "catalogs"];
    public static readonly IReadOnlyList<string> Placements = Enum.GetNames<FormFolderMenuPlacement>();

    public static string Invalid() => ErrorMessages.Get(FormFolderErrors.RequestInvalid.Code);
    public static bool IsRevision(long? revision) => revision is >= 1 and <= 9007199254740991;
    public static bool IsFolderList(IReadOnlyList<Guid>? ids) =>
        ids is { Count: <= MaxFoldersPerItem } && !ids.Contains(Guid.Empty) && ids.Distinct().Count() == ids.Count;
}

internal sealed class ListFormFoldersRequestValidator : AbstractValidator<ListFormFoldersRequest>
{
    public ListFormFoldersRequestValidator() =>
        RuleFor(request => request.Kind).Must(kind => kind is not null && FormFolderRequestValidation.Kinds.Contains(kind))
            .WithMessage(_ => FormFolderRequestValidation.Invalid());
}

internal sealed class CreateFormFolderRequestValidator : AbstractValidator<CreateFormFolderRequest>
{
    public CreateFormFolderRequestValidator()
    {
        RuleFor(request => request.Name).Must(BusinessFormFolder.IsValidName).WithMessage(_ => ErrorMessages.Get(FormFolderErrors.NameInvalid.Code));
        RuleFor(request => request.ParentId).NotEqual(Guid.Empty).WithMessage(_ => FormFolderRequestValidation.Invalid());
    }
}

internal sealed class UpdateFormFolderRequestValidator : AbstractValidator<UpdateFormFolderRequest>
{
    public UpdateFormFolderRequestValidator()
    {
        RuleFor(request => request.ExpectedRevision).Must(FormFolderRequestValidation.IsRevision).WithMessage(_ => FormFolderRequestValidation.Invalid());
        RuleFor(request => request.Name).Must(BusinessFormFolder.IsValidName).WithMessage(_ => ErrorMessages.Get(FormFolderErrors.NameInvalid.Code));
        RuleFor(request => request.ParentId).NotEqual(Guid.Empty).WithMessage(_ => FormFolderRequestValidation.Invalid());
    }
}

internal sealed class DeleteFormFolderRequestValidator : AbstractValidator<DeleteFormFolderRequest>
{
    public DeleteFormFolderRequestValidator() =>
        RuleFor(request => request.ExpectedRevision).Must(FormFolderRequestValidation.IsRevision).WithMessage(_ => FormFolderRequestValidation.Invalid());
}

internal sealed class SetFormFolderMenuRequestValidator : AbstractValidator<SetFormFolderMenuRequest>
{
    public SetFormFolderMenuRequestValidator()
    {
        RuleFor(request => request.ExpectedRevision).Must(FormFolderRequestValidation.IsRevision).WithMessage(_ => FormFolderRequestValidation.Invalid());
        RuleFor(request => request.Placement).Must(placement => placement is not null && FormFolderRequestValidation.Placements.Contains(placement))
            .WithMessage(_ => FormFolderRequestValidation.Invalid());
        // The icon is checked by the folder itself, so its error keeps its own code (Forms.Folder.IconInvalid) on «icon».
    }
}

internal sealed class SetFormItemFoldersRequestValidator : AbstractValidator<SetFormItemFoldersRequest>
{
    public SetFormItemFoldersRequestValidator()
    {
        RuleFor(request => request.ExpectedFolderIds).Must(FormFolderRequestValidation.IsFolderList).WithMessage(_ => FormFolderRequestValidation.Invalid());
        RuleFor(request => request.FolderIds).Must(FormFolderRequestValidation.IsFolderList).WithMessage(_ => FormFolderRequestValidation.Invalid());
    }
}
