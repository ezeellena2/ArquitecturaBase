using ArquitecturaBase.Domain.Results;

namespace ArquitecturaBase.Domain.Modules.BusinessForms;

public static class FormCatalogImportErrors
{
    public static readonly Error FileUnreadable = Error.Validation("Forms.CatalogImport.FileUnreadable", "We could not read the file. Upload an XLSX or UTF-8 CSV file.");
    public static readonly Error FileTooLarge = Error.Validation("Forms.CatalogImport.FileTooLarge", "The file exceeds the allowed size.");
    public static readonly Error TooManyRows = Error.Validation("Forms.CatalogImport.TooManyRows", "The sheet has more than 10,000 rows.");
    public static readonly Error TooManyColumns = Error.Validation("Forms.CatalogImport.TooManyColumns", "The sheet has too many columns.");
    public static readonly Error MissingColumn = Error.Validation("Forms.CatalogImport.MissingColumn", "A required column is missing.");
    public static readonly Error DuplicateColumn = Error.Validation("Forms.CatalogImport.DuplicateColumn", "Two columns have the same header.");
    public static readonly Error UnknownColumn = Error.Validation("Forms.CatalogImport.UnknownColumn", "This column is not part of the catalog: it is ignored.");
    public static readonly Error SheetChanged = Error.Conflict("Forms.CatalogImport.SheetChanged", "This is not the sheet you reviewed. Review it again.");
    public static readonly Error ConfirmationRequired = Error.Validation("Forms.CatalogImport.ConfirmationRequired", "Confirm the deactivations or the rows with errors before applying.");
    public static readonly Error RequiredValue = Error.Validation("Forms.CatalogImport.RequiredValue", "Missing: it is required.");
    public static readonly Error DuplicateCode = Error.Validation("Forms.CatalogImport.DuplicateCode", "Duplicate: it is also in another row.");
    public static readonly Error TextTooLong = Error.Validation("Forms.CatalogImport.TextTooLong", "Longer than 30 characters: WhatsApp shows up to 30.");
    public static readonly Error CodeTooLong = Error.Validation("Forms.CatalogImport.CodeTooLong", "The code is longer than 80 characters.");
    public static readonly Error DetailTooLong = Error.Validation("Forms.CatalogImport.DetailTooLong", "The detail is longer than 300 characters.");
    public static readonly Error ActiveInvalid = Error.Validation("Forms.CatalogImport.ActiveInvalid", "Use Yes or No to indicate whether the option is active.");
    public static readonly Error DuplicateText = Error.Validation("Forms.CatalogImport.DuplicateText", "This text also appears in another option.");
    public static readonly Error FormulaValueMissing = Error.Validation("Forms.CatalogImport.FormulaValueMissing", "The formula has no saved value. Save the sheet and upload it again.");
    public static readonly Error CellUnreadable = Error.Validation("Forms.CatalogImport.CellUnreadable", "We could not read the value of this cell.");
    public static Error MissingHeader(string column) => MissingColumn with
    { Metadata = new Dictionary<string, object?> { ["column"] = column } };
    public static Error DuplicateHeader(string column) => DuplicateColumn with
    { Metadata = new Dictionary<string, object?> { ["column"] = column } };
}
