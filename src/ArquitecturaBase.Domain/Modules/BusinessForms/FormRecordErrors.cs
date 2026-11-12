using ArquitecturaBase.Domain.Results;

namespace ArquitecturaBase.Domain.Modules.BusinessForms;

public static class FormRecordErrors
{
    public static readonly Error StateConflict = Error.Conflict("Forms.Record.StateConflict", "The response workflow state changed.");
    public static readonly Error ExportTooLarge = Error.Validation("Forms.Record.ExportTooLarge", "An export cannot exceed ten thousand responses.");
    public static readonly Error ExportTooManyRows = Error.Validation("Forms.Record.ExportTooManyRows", "An export cannot exceed one hundred thousand rows.");
    public static readonly Error ExportBudgetExceeded = Error.Validation("Forms.Record.ExportBudgetExceeded", "The export exceeded its processing budget.");
    public static readonly Error FilterInvalid = Error.Validation("Forms.Record.FilterInvalid", "The response filter is invalid.");
    public static readonly Error PeriodInvalid = Error.Validation("Forms.Record.PeriodInvalid", "The dashboard period must be day, week or month.");
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
    public static readonly Error RecipientDataMissing = Error.Conflict("Forms.Record.RecipientDataMissing", "The recipient is missing data required by the question filter.");
    public static readonly Error CatalogFilterUnavailable = Error.Conflict("Forms.Record.CatalogFilterUnavailable", "The question filter references an unavailable field or person attribute.");
    public static readonly Error CatalogDependsOnAnswer = Error.Conflict("Forms.Record.CatalogDependsOnAnswer", "The question options depend on another answer, which is not given.");
    public static readonly Error IssuedNotAmendable = Error.Conflict("Forms.Record.IssuedNotAmendable", "The response has an issued document or one being issued: void it and load another.");
    public static readonly Error Voiding = Error.Conflict("Forms.Record.Voiding", "The response is already being voided.");
    public static readonly Error AmendUnavailable = Error.Conflict("Forms.Record.AmendUnavailable", "The response changed and can no longer be corrected or voided.");
    public static readonly Error ReasonRequired = Error.Validation("Forms.Record.ReasonRequired", "Correcting or voiding a response requires a reason.");
    public static readonly Error ReasonInvalid = Error.Validation("Forms.Record.ReasonInvalid", "The reason cannot exceed five hundred characters.");
    // DOC1: the fixed validations of a question bound by a library standard, on every capture.
    public static readonly Error BankAccountInvalid = Error.Validation("Forms.Record.BankAccountInvalid", "The account is neither a CBU or CVU whose check digits add up nor a valid alias.");
    public static readonly Error TaxIdInvalid = Error.Validation("Forms.Record.TaxIdInvalid", "The CUIT or CUIL does not have eleven digits with a valid check digit.");
}
