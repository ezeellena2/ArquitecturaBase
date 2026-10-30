using ArquitecturaBase.Domain.Results;

namespace ArquitecturaBase.Domain.Modules.BusinessForms;

/// <summary>Loading a response in the app: only the active published version of an active form.</summary>
public static class FormEntryErrors
{
    public static readonly Error VersionChanged = Error.Conflict("Forms.Entry.VersionChanged", "A newer version of the form was published.");
    public static readonly Error Unavailable = Error.Conflict("Forms.Entry.Unavailable", "The form was deactivated and can no longer be loaded.");
    public static readonly Error QuestionNotFound = Error.NotFound("Forms.Entry.QuestionNotFound", "The question has no catalog in this version.");
}
