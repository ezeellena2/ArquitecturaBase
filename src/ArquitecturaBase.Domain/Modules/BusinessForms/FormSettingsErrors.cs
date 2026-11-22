using ArquitecturaBase.Domain.Results;

namespace ArquitecturaBase.Domain.Modules.BusinessForms;

public static class FormSettingsErrors
{
    public static readonly Error RevisionConflict = Error.Conflict("Forms.Settings.RevisionConflict", "Another person changed the forms settings. Reload them.");
    public static readonly Error ContentInvalid = Error.Validation("Forms.Settings.ContentInvalid", "A forms setting is out of its range.");
}
