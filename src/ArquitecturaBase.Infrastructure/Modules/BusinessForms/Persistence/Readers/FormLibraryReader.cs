using ArquitecturaBase.Application.Modules.BusinessForms.Interfaces.Persistence;
using ArquitecturaBase.Domain.Modules.BusinessForms;
using ArquitecturaBase.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace ArquitecturaBase.Infrastructure.Modules.BusinessForms.Persistence.Readers;

internal sealed class FormLibraryReader(ApplicationDbContext db) : IFormLibraryReader
{
    public async Task<IReadOnlyDictionary<string, int>> ListCopyCountsAsync(IReadOnlyList<string> standardKeys, CancellationToken cancellationToken) =>
        await db.Set<BusinessFormDefinition>().AsNoTracking().Where(definition => definition.StandardKey != null && standardKeys.Contains(definition.StandardKey))
            .GroupBy(definition => definition.StandardKey!).Select(group => new { Key = group.Key, Count = group.Count() })
            .ToDictionaryAsync(row => row.Key, row => row.Count, cancellationToken);

    public async Task<IReadOnlyDictionary<string, Guid>> ListCatalogIdsByKeyAsync(IReadOnlyList<string> catalogKeys, CancellationToken cancellationToken) =>
        await db.Set<BusinessFormCatalog>().AsNoTracking().Where(catalog => catalog.IsActive && catalogKeys.Contains(catalog.Key))
            .ToDictionaryAsync(catalog => catalog.Key, catalog => catalog.Id, cancellationToken);
}
