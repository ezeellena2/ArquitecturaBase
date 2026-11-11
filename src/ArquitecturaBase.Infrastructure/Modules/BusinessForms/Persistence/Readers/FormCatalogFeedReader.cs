using ArquitecturaBase.Application.Modules.BusinessForms.Interfaces.Persistence;
using ArquitecturaBase.Application.Modules.BusinessForms.Models;
using ArquitecturaBase.Domain.Modules.BusinessForms;
using ArquitecturaBase.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace ArquitecturaBase.Infrastructure.Modules.BusinessForms.Persistence.Readers;

internal sealed class FormCatalogFeedReader(ApplicationDbContext dbContext) : IFormCatalogFeedReader
{
    public async Task<IReadOnlyList<FormCatalogFeedRow>> ListAsync(IReadOnlyCollection<string> keys, CancellationToken cancellationToken) =>
        await dbContext.Set<BusinessFormCatalog>().AsNoTracking()
            .Where(catalog => catalog.Kind == BusinessFormCatalogKind.External && keys.Contains(catalog.Key))
            .Select(catalog => new FormCatalogFeedRow(catalog.Key, catalog.FeedRefreshedAtUtc, catalog.FeedFailedAtUtc, catalog.FeedLeaseUntilUtc))
            .ToArrayAsync(cancellationToken);
}
