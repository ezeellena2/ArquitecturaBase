using ArquitecturaBase.Domain.Results;

namespace ArquitecturaBase.Domain.Modules.BusinessForms;

public static class FormRecordErrors
{
    public static readonly Error NotFound = Error.NotFound("Forms.Record.NotFound", "The form response is unavailable.");
    public static readonly Error CatalogUnavailable = Error.Conflict("Forms.Record.CatalogUnavailable", "The catalog is unavailable.");
    public static readonly Error CatalogTooLarge = Error.Conflict("Forms.Record.CatalogTooLarge", "The catalog exceeds two hundred options.");
    public static readonly Error CatalogEmpty = Error.Conflict("Forms.Record.CatalogEmpty", "A required catalog has no options.");
    public static readonly Error CatalogOptionInvalid = Error.Conflict("Forms.Record.CatalogOptionInvalid", "A catalog option cannot be represented in the form.");
    public static readonly Error CatalogItemInactive = Error.Validation("Forms.Record.CatalogItemInactive", "An offered manual option is no longer active.");
    public static readonly Error InvalidResponse = Error.Validation("Forms.Record.InvalidResponse", "The submitted response does not match the published form.");
    public static readonly Error ReplayConflict = Error.Conflict("Forms.Record.ReplayConflict", "This Flow session was already used with a different response.");
    public static readonly Error AuthorizationChanged = Error.Forbidden("Forms.Record.AuthorizationChanged", "The linked account can no longer submit this form.");
}
