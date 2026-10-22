using ArquitecturaBase.Application.Modules.BusinessForms.Interfaces.Persistence;
using ArquitecturaBase.Domain.Modules.BusinessForms;

namespace ArquitecturaBase.Application.UnitTests.TestFeatures.Modules.BusinessForms;

internal sealed class FormCatalogCapacityRepository(BusinessFormCatalog? catalog = null,
    IEnumerable<BusinessFormCatalogItem>? items = null) : IFormCatalogRepository
{
    public BusinessFormCatalog? Catalog { get; private set; } = catalog;
    public List<BusinessFormCatalogItem> Items { get; } = items?.ToList() ?? [];
    public List<Guid> Locks { get; } = [];

    public Task LockAsync(Guid id, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        Locks.Add(id);
        return Task.CompletedTask;
    }

    public Task<BusinessFormCatalog?> GetAsync(Guid id, CancellationToken cancellationToken) =>
        Task.FromResult<BusinessFormCatalog?>(Catalog?.Id == id ? Catalog : null);

    public Task<BusinessFormCatalog?> GetByProposalAsync(Guid proposalId, CancellationToken cancellationToken) =>
        Task.FromResult<BusinessFormCatalog?>(null);

    public Task<List<BusinessFormCatalogItem>> ListItemsAsync(Guid id, CancellationToken cancellationToken) =>
        Task.FromResult(Items.Where(item => item.CatalogId == id).ToList());

    public void Add(BusinessFormCatalog value) => Catalog = value;

    public void AddItems(IEnumerable<BusinessFormCatalogItem> values) => Items.AddRange(values);
}
