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
    public static readonly Error ContentInvalid = Error.Validation("Forms.Catalog.ContentInvalid", "The catalog content is invalid.");
}
