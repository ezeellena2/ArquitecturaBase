using ArquitecturaBase.Application.Modules.BusinessForms.Interfaces.Persistence;
using ArquitecturaBase.Domain.Modules.BusinessForms;
using ArquitecturaBase.Infrastructure.Persistence;
using ArquitecturaBase.Infrastructure.Persistence.Extensions;
using Microsoft.EntityFrameworkCore;

namespace ArquitecturaBase.Infrastructure.Modules.BusinessForms.Persistence.Repositories;

internal sealed class FormCatalogRepository(ApplicationDbContext dbContext) : IFormCatalogRepository
{
    public Task LockAsync(Guid catalogId, CancellationToken cancellationToken) =>
        dbContext.AcquireAdvisoryLocksAsync([FormAdvisoryLockKeys.Catalog(catalogId)], cancellationToken);

    public Task<BusinessFormCatalog?> GetAsync(Guid id, CancellationToken cancellationToken) =>
        dbContext.Set<BusinessFormCatalog>().SingleOrDefaultAsync(value => value.Id == id, cancellationToken);

    public Task<BusinessFormCatalog?> GetByProposalAsync(Guid proposalId, CancellationToken cancellationToken) =>
        dbContext.Set<BusinessFormCatalog>().SingleOrDefaultAsync(value => value.ProposalId == proposalId, cancellationToken);

    public Task<List<BusinessFormCatalogItem>> ListItemsAsync(Guid catalogId, CancellationToken cancellationToken) =>
        dbContext.Set<BusinessFormCatalogItem>().Where(value => value.CatalogId == catalogId).ToListAsync(cancellationToken);

    public void Add(BusinessFormCatalog catalog)
    {
        dbContext.RequireTransaction();
        dbContext.Set<BusinessFormCatalog>().Add(catalog);
    }

    public void AddItems(IEnumerable<BusinessFormCatalogItem> items)
    {
        dbContext.RequireTransaction();
        dbContext.Set<BusinessFormCatalogItem>().AddRange(items);
    }
}
