using ArquitecturaBase.Domain.Results;

namespace ArquitecturaBase.Domain.Modules.BusinessForms;

public static class FormLibraryErrors
{
    public static readonly Error StandardNotFound = Error.NotFound("Forms.Library.StandardNotFound", "The standard is not in the library.");
    public static readonly Error CatalogUnavailable = Error.Conflict("Forms.Library.CatalogUnavailable", "A catalog the standard needs is not available.");
}
