using ArquitecturaBase.Domain.Results;

namespace ArquitecturaBase.Domain.Modules.BusinessForms;

public static class FormDraftErrors
{
    public static readonly Error RevisionConflict = Error.Conflict(
        "Forms.Draft.RevisionConflict", "The draft changed. Reload it before saving.");

    public static readonly Error QuestionTypeUnavailable = Error.Validation(
        "Forms.Draft.QuestionTypeUnavailable", "The question type is not available in this stage.");

    public static readonly Error IdentityInvalid = Error.Validation(
        "Forms.Draft.IdentityInvalid", "The identity does not belong to this draft or question.");

    public static readonly Error IdentityRetired = Error.Conflict(
        "Forms.Draft.IdentityRetired", "A retired identity cannot be reused.");

    public static readonly Error QuestionConfigurationInvalid = Error.Validation(
        "Forms.Draft.QuestionConfigurationInvalid", "The question configuration is not valid for its type.");

    public static readonly Error ChoiceLabelRequired = Error.Validation(
        "Forms.Draft.ChoiceLabelRequired", "A choice requires a nonblank label.");
}
