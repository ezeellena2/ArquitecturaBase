using ArquitecturaBase.Application.Modules.BusinessForms.Interfaces.Persistence;
using ArquitecturaBase.Domain.Modules.BusinessForms;
using ArquitecturaBase.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace ArquitecturaBase.Infrastructure.Modules.BusinessForms.Persistence.Readers;

internal sealed class FormCatalogItemCodeReader(ApplicationDbContext dbContext) : IFormCatalogItemCodeReader
{
    public async Task<IReadOnlyDictionary<Guid, string>> ListAsync(IReadOnlyCollection<Guid> itemIds, CancellationToken cancellationToken) =>
        itemIds.Count == 0 ? new Dictionary<Guid, string>() : await dbContext.Set<BusinessFormCatalogItem>().AsNoTracking()
            .Where(item => itemIds.Contains(item.Id)).ToDictionaryAsync(item => item.Id, item => item.Code, cancellationToken);
}
