using ArquitecturaBase.Application.Modules.BusinessForms.Interfaces.Persistence;
using ArquitecturaBase.Domain.Modules.BusinessForms;
using ArquitecturaBase.Infrastructure.Persistence;
using ArquitecturaBase.Infrastructure.Persistence.Extensions;
using Microsoft.EntityFrameworkCore;

namespace ArquitecturaBase.Infrastructure.Modules.BusinessForms.Persistence.Readers;

internal sealed class FormLibraryReader(ApplicationDbContext db) : IFormLibraryReader
{
    public async Task<IReadOnlyDictionary<string, int>> ListCopyCountsAsync(IReadOnlyList<string> standardKeys, CancellationToken cancellationToken) =>
        await db.Set<BusinessFormDefinition>().AsNoTracking().Where(definition => definition.StandardKey != null && standardKeys.Contains(definition.StandardKey))
            .GroupBy(definition => definition.StandardKey!).Select(group => new { Key = group.Key, Count = group.Count() })
            .ToDictionaryAsync(row => row.Key, row => row.Count, cancellationToken);

    public async Task<IReadOnlyDictionary<string, ArquitecturaBase.Application.Modules.BusinessForms.Models.FormLibraryCatalogRow>> ListCatalogsByKeyAsync(
        IReadOnlyList<string> catalogKeys, CancellationToken cancellationToken) =>
        await db.Set<BusinessFormCatalog>().AsNoTracking().Where(catalog => catalogKeys.Contains(catalog.Key))
            .ToDictionaryAsync(catalog => catalog.Key, catalog => new ArquitecturaBase.Application.Modules.BusinessForms.Models.FormLibraryCatalogRow(catalog.Id, catalog.IsActive), cancellationToken);

    public async Task<IReadOnlySet<string>> ListPrefixesAsync(IReadOnlyList<string> stems, CancellationToken cancellationToken)
    {
        // A prefix has only letters and digits, so a stem never carries a LIKE wildcard; one that does would be a standard's bug.
        if (stems.Any(stem => stem.Length == 0 || !stem.All(char.IsAsciiLetterOrDigit))) throw new ArgumentException("A prefix stem has only letters and digits.", nameof(stems));
        var patterns = stems.Distinct(StringComparer.Ordinal).Select(stem => stem + "%").ToArray();
        if (patterns.Length == 0) return new HashSet<string>(StringComparer.Ordinal);
        var prefixes = await db.Set<BusinessFormDefinition>().IgnoreQueryFilters([ModelBuilderExtensions.SoftDeleteFilter]).AsNoTracking()
            .Where(definition => patterns.Any(pattern => EF.Functions.Like(definition.Prefix, pattern))).Select(definition => definition.Prefix).ToListAsync(cancellationToken);
        return prefixes.ToHashSet(StringComparer.Ordinal);
    }
}
