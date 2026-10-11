using ArquitecturaBase.Domain.Results;

namespace ArquitecturaBase.Domain.Modules.BusinessForms;

public static class FormRecordErrors
{
    public static readonly Error InvalidResponse = Error.Validation("Forms.Record.InvalidResponse", "The submitted response does not match the published form.");
    public static readonly Error ReplayConflict = Error.Conflict("Forms.Record.ReplayConflict", "This Flow session was already used with a different response.");
    public static readonly Error AuthorizationChanged = Error.Forbidden("Forms.Record.AuthorizationChanged", "The linked account can no longer submit this form.");
}
