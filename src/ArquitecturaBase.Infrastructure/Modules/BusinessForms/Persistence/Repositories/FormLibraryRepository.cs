using ArquitecturaBase.Application.Modules.BusinessForms.Interfaces.Persistence;
using ArquitecturaBase.Domain.Modules.BusinessForms;
using ArquitecturaBase.Infrastructure.Persistence;
using ArquitecturaBase.Infrastructure.Persistence.Extensions;

namespace ArquitecturaBase.Infrastructure.Modules.BusinessForms.Persistence.Repositories;

internal sealed class FormLibraryRepository(ApplicationDbContext dbContext) : IFormLibraryRepository
{
    public void AddCatalog(BusinessFormCatalog catalog) { dbContext.RequireTransaction(); dbContext.Set<BusinessFormCatalog>().Add(catalog); }
    public void AddItems(IReadOnlyList<BusinessFormCatalogItem> items) { dbContext.RequireTransaction(); dbContext.Set<BusinessFormCatalogItem>().AddRange(items); }
}
