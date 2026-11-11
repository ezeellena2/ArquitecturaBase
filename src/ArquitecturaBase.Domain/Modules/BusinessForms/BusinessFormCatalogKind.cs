namespace ArquitecturaBase.Domain.Modules.BusinessForms;

public enum BusinessFormCatalogKind
{
    Manual,
    Filters,
    /// <summary>A read-only local copy of a list an installed module feeds: the app does not edit, import or delete it.</summary>
    External,
}
