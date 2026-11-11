using ArquitecturaBase.Domain.Modules.BusinessForms;

namespace ArquitecturaBase.Application.Modules.BusinessForms.Interfaces.Persistence;

/// <summary>BI1-A: what a copy of a library standard adds in the same save as its draft: the catalogs it brings and their first options.</summary>
public interface IFormLibraryRepository
{
    void AddCatalog(BusinessFormCatalog catalog);
    void AddItems(IReadOnlyList<BusinessFormCatalogItem> items);
}
