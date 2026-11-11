using ArquitecturaBase.Application.Modules.BusinessForms.Interfaces.Persistence;
using ArquitecturaBase.Domain.Modules.BusinessForms;
using ArquitecturaBase.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace ArquitecturaBase.Infrastructure.Modules.BusinessForms.Persistence.Repositories;

internal sealed class FormCatalogFeedRepository(ApplicationDbContext dbContext) : IFormCatalogFeedRepository
{
    public Task<BusinessFormCatalog?> GetByKeyAsync(string key, CancellationToken cancellationToken) =>
        dbContext.Set<BusinessFormCatalog>().SingleOrDefaultAsync(value => value.Key == key, cancellationToken);
}
