using ArquitecturaBase.Domain.Results;

namespace ArquitecturaBase.Domain.Modules.BusinessForms;

public static class FormCatalogErrors
{
    public static readonly Error NotFound = Error.NotFound("Forms.Catalog.NotFound", "The catalog was not found.");
    public static readonly Error RevisionConflict = Error.Conflict("Forms.Catalog.RevisionConflict", "The catalog changed. Reload it and try again.");
    public static readonly Error Inactive = Error.Conflict("Forms.Catalog.Inactive", "The catalog is inactive.");
    public static readonly Error ItemNotFound = Error.NotFound("Forms.Catalog.ItemNotFound", "The catalog item was not found.");
    public static readonly Error ManualItemsUnavailable = Error.Conflict("Forms.Catalog.ManualItemsUnavailable", "This catalog does not contain manual items.");
    public static readonly Error FiltersUnavailable = Error.Conflict("Forms.Catalog.FiltersUnavailable", "This catalog does not contain filters.");
    public static readonly Error DuplicateItemCode = Error.Conflict("Forms.Catalog.DuplicateItemCode", "An item with this code already exists.");
    public static readonly Error TooManyItems = Error.Validation("Forms.Catalog.TooManyItems", "A catalog can contain up to 10,000 items.");
    public static readonly Error ItemValueRequired = Error.Validation("Forms.Catalog.ItemValueRequired", "Enter the option value.");
    public static readonly Error ItemValueInvalid = Error.Validation("Forms.Catalog.ItemValueInvalid", "The value does not match the catalog type.");
    public static readonly Error ContentInvalid = Error.Validation("Forms.Catalog.ContentInvalid", "The catalog content is invalid.");
    public static readonly Error TooManyFields = Error.Validation("Forms.Catalog.TooManyFields", "A catalog can have up to 20 fields.");
    public static readonly Error FieldKeyDuplicate = Error.Validation("Forms.Catalog.FieldKeyDuplicate", "A field with this key already exists.");
    public static readonly Error FieldInvalid = Error.Validation("Forms.Catalog.FieldInvalid", "The field type, rules or role are invalid.");
    public static readonly Error FieldInUse = Error.Conflict("Forms.Catalog.FieldInUse", "The field is in use.");
    public static readonly Error FieldRemovalNotConfirmed = Error.Validation("Forms.Catalog.FieldRemovalNotConfirmed", "Confirm removal of the field and its values.");
    public static readonly Error FieldTypeNotConvertible = Error.Validation("Forms.Catalog.FieldTypeNotConvertible", "Some values cannot be converted to the new type.");
    public static readonly Error FieldValuesMissing = Error.Validation("Forms.Catalog.FieldValuesMissing", "Some active items have no value for the required field.");
    public static readonly Error FieldValueInvalid = Error.Validation("Forms.Catalog.FieldValueInvalid", "The value does not match the field type.");
    public static readonly Error FieldValueRequired = Error.Validation("Forms.Catalog.FieldValueRequired", "Enter a value for the required field.");
    public static readonly Error FieldValueDuplicate = Error.Validation("Forms.Catalog.FieldValueDuplicate", "The unique field value is already used by another active item.");
    public static readonly Error SourceInUse = Error.Conflict("Forms.Catalog.SourceInUse", "A field or person attribute uses this catalog as its source.");
    public static readonly Error PresentationInvalid = Error.Validation("Forms.Catalog.PresentationInvalid", "The title needs at least one existing field.");

    public static Error FieldDetails(Error error, IReadOnlyDictionary<string, object?> metadata) => error with { Metadata = metadata };
}
