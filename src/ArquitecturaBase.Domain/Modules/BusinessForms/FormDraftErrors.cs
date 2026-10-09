using ArquitecturaBase.Domain.Results;

namespace ArquitecturaBase.Domain.Modules.BusinessForms;

public static class FormDraftErrors
{
    public static readonly Error RevisionConflict = Error.Conflict(
        "Forms.Draft.RevisionConflict", "The draft changed. Reload it before saving.");

    public static readonly Error QuestionTypeUnavailable = Error.Validation(
        "Forms.Draft.QuestionTypeUnavailable", "The question type is not available in this stage.");
}
