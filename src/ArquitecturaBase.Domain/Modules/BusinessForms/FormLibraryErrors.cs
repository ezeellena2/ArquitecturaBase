using ArquitecturaBase.Domain.Results;

namespace ArquitecturaBase.Domain.Modules.BusinessForms;

public static class FormLibraryErrors
{
    public static readonly Error StandardNotFound = Error.NotFound("Forms.Library.StandardNotFound", "The standard is not in the library.");
    public static readonly Error CatalogUnavailable = Error.Conflict("Forms.Library.CatalogUnavailable", "A catalog the standard needs is not available.");
    public static readonly Error CatalogAppeared = Error.Conflict("Forms.Library.CatalogAppeared", "Someone just added a catalog of this standard; try again.");
}
