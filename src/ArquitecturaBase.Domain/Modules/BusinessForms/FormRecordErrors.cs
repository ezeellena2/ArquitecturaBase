using ArquitecturaBase.Domain.Results;

namespace ArquitecturaBase.Domain.Modules.BusinessForms;

public static class FormRecordErrors
{
    public static readonly Error StateConflict = Error.Conflict("Forms.Record.StateConflict", "The response workflow state changed.");
    public static readonly Error ExportTooLarge = Error.Validation("Forms.Record.ExportTooLarge", "An export cannot exceed ten thousand responses.");
    public static readonly Error ExportBudgetExceeded = Error.Validation("Forms.Record.ExportBudgetExceeded", "The export exceeded its processing budget.");
    public static readonly Error FilterInvalid = Error.Validation("Forms.Record.FilterInvalid", "The response filter is invalid.");
    public static readonly Error NotFound = Error.NotFound("Forms.Record.NotFound", "The form response is unavailable.");
    public static readonly Error CatalogUnavailable = Error.Conflict("Forms.Record.CatalogUnavailable", "The catalog is unavailable.");
    public static readonly Error CatalogTooLarge = Error.Conflict("Forms.Record.CatalogTooLarge", "The catalog exceeds two hundred options.");
    public static readonly Error CatalogTooLargeForMultiple = Error.Conflict("Forms.Record.CatalogTooLargeForMultiple", "A multiple-selection offer exceeds twenty options.");
    public static readonly Error CatalogInsufficientOptions = Error.Conflict("Forms.Record.CatalogInsufficientOptions", "The available options cannot meet the question minimum.");
    public static readonly Error CatalogEmpty = Error.Conflict("Forms.Record.CatalogEmpty", "A required catalog has no options.");
    public static readonly Error CatalogOptionInvalid = Error.Conflict("Forms.Record.CatalogOptionInvalid", "A catalog option cannot be represented in the form.");
    public static readonly Error CatalogItemInactive = Error.Validation("Forms.Record.CatalogItemInactive", "An offered manual option is no longer active.");
    public static readonly Error InvalidResponse = Error.Validation("Forms.Record.InvalidResponse", "The submitted response does not match the published form.");
    public static readonly Error ReplayConflict = Error.Conflict("Forms.Record.ReplayConflict", "This Flow session was already used with a different response.");
    public static readonly Error AuthorizationChanged = Error.Forbidden("Forms.Record.AuthorizationChanged", "The linked account can no longer submit this form.");
}
