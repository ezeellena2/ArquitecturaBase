using ArquitecturaBase.Application.Modules.BusinessForms.Interfaces.Persistence;
using ArquitecturaBase.Domain.Modules.BusinessForms;

namespace ArquitecturaBase.Application.UnitTests.TestFeatures.Modules.BusinessForms;

internal sealed class FormCatalogActivationRepository(BusinessFormCatalog catalog) : IFormCatalogRepository
{
    public Task LockKeyAsync(string key, CancellationToken cancellationToken) => Task.CompletedTask;
    public Task<Guid?> FindIdByKeyAsync(string key, CancellationToken cancellationToken) => Task.FromResult<Guid?>(null);
    public Task LockAsync(Guid id, CancellationToken ct) => Task.CompletedTask;
    public Task<BusinessFormCatalog?> GetByProposalAsync(Guid id, CancellationToken ct) => Task.FromResult<BusinessFormCatalog?>(null);
    public Task<BusinessFormCatalog?> GetAsync(Guid id, CancellationToken ct) => Task.FromResult<BusinessFormCatalog?>(id == catalog.Id ? catalog : null);
    public Task<List<BusinessFormCatalogItem>> ListItemsAsync(Guid id, CancellationToken ct) => Task.FromResult(new List<BusinessFormCatalogItem>());
    public void Add(BusinessFormCatalog value) => throw new NotSupportedException();
    public void AddItems(IEnumerable<BusinessFormCatalogItem> items) { }
    public void RemoveItems(IEnumerable<BusinessFormCatalogItem> items) => throw new NotSupportedException();
}
