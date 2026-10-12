using ArquitecturaBase.Domain.Modules.BusinessForms;

namespace ArquitecturaBase.Application.Modules.BusinessForms.Interfaces.Persistence;

public interface IFormCatalogRepository
{
    Task LockAsync(Guid catalogId, CancellationToken cancellationToken);
    Task<BusinessFormCatalog?> GetAsync(Guid id, CancellationToken cancellationToken);
    Task<List<BusinessFormCatalogItem>> ListItemsAsync(Guid catalogId, CancellationToken cancellationToken);
    void Add(BusinessFormCatalog catalog);
    void AddItems(IEnumerable<BusinessFormCatalogItem> items);
}
