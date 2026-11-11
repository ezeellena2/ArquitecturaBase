using ArquitecturaBase.Domain.Modules.BusinessForms;

namespace ArquitecturaBase.Application.Modules.BusinessForms.Interfaces.Persistence;

public interface IFormCatalogRepository
{
    Task LockAsync(Guid catalogId, CancellationToken cancellationToken);

    /// <summary>The lock of a catalog a module feeds, by its key: it serializes creating it. Goes before <see cref="LockAsync"/>.</summary>
    Task LockKeyAsync(string key, CancellationToken cancellationToken);

    /// <summary>The id of the catalog with that key, if there is one; the caller takes its lock before reading it.</summary>
    Task<Guid?> FindIdByKeyAsync(string key, CancellationToken cancellationToken);
    Task<BusinessFormCatalog?> GetAsync(Guid id, CancellationToken cancellationToken);
    Task<BusinessFormCatalog?> GetByProposalAsync(Guid proposalId, CancellationToken cancellationToken);
    Task<List<BusinessFormCatalogItem>> ListItemsAsync(Guid catalogId, CancellationToken cancellationToken);
    void Add(BusinessFormCatalog catalog);
    void AddItems(IEnumerable<BusinessFormCatalogItem> items);
    void RemoveItems(IEnumerable<BusinessFormCatalogItem> items);
}
