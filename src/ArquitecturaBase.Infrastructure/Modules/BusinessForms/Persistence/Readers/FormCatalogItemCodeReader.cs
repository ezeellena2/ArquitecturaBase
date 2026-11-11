using ArquitecturaBase.Application.Modules.BusinessForms.Interfaces.Persistence;
using ArquitecturaBase.Domain.Modules.BusinessForms;
using ArquitecturaBase.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace ArquitecturaBase.Infrastructure.Modules.BusinessForms.Persistence.Readers;

internal sealed class FormCatalogItemCodeReader(ApplicationDbContext dbContext) : IFormCatalogItemCodeReader
{
    public async Task<IReadOnlyDictionary<Guid, string>> ListAsync(IReadOnlyCollection<Guid> itemIds, CancellationToken cancellationToken) =>
        itemIds.Count == 0 ? new Dictionary<Guid, string>() : await dbContext.Set<BusinessFormCatalogItem>().AsNoTracking()
            .Where(item => itemIds.Contains(item.Id)).Select(item => new { item.Id, item.Code }).ToDictionaryAsync(item => item.Id, item => item.Code, cancellationToken);

    public async Task<IReadOnlyDictionary<string, Guid>> ListActiveAsync(Guid catalogId, IReadOnlyCollection<string> codes, CancellationToken cancellationToken) =>
        codes.Count == 0 ? new Dictionary<string, Guid>(StringComparer.Ordinal) : await dbContext.Set<BusinessFormCatalogItem>().AsNoTracking()
            .Where(item => item.CatalogId == catalogId && item.IsActive && codes.Contains(item.Code)).Select(item => new { item.Code, item.Id })
            .ToDictionaryAsync(item => item.Code, item => item.Id, StringComparer.Ordinal, cancellationToken);
}
