using ArquitecturaBase.Application.Modules.BusinessForms.Models;
using ArquitecturaBase.Domain.Modules.BusinessForms;
using Microsoft.EntityFrameworkCore;

namespace ArquitecturaBase.Infrastructure.Modules.BusinessForms.Persistence.Readers;

internal sealed partial class FormRecordReader
{
    public async Task<IReadOnlyDictionary<Guid, FormResponseVersionRow>> ListVersionsAsync(IReadOnlyCollection<Guid> versionIds, CancellationToken ct)
    {
        if (versionIds.Count == 0) return new Dictionary<Guid, FormResponseVersionRow>();
        var rows = await (from version in dbContext.Set<BusinessFormVersion>().AsNoTracking()
            join operation in dbContext.Set<BusinessFormPublicationOperation>().AsNoTracking() on version.Id equals operation.VersionId
            where versionIds.Contains(version.Id) && operation.DefinitionId == version.DefinitionId
            select new { version.Id, version.Number, version.NameJson, version.SchemaJson, version.Culture, version.PublishedCultures }).ToArrayAsync(ct);
        var versions = rows.Select(row =>
        {
            // The languages are the ones the version sealed: reading them does not open the protected manifest (F27).
            var cultures = row.PublishedCultures ?? [];
            if (row.Culture is not ("es" or "en")
                || !cultures.Contains(row.Culture, StringComparer.Ordinal) || cultures.Any(culture => culture is not ("es" or "en")))
                throw new InvalidOperationException("The sealed form publication cultures are invalid.");
            return new FormResponseVersionRow(row.Id, row.Number, row.NameJson!, row.SchemaJson, row.Culture, cultures);
        }).ToDictionary(version => version.Id);
        if (versions.Count != versionIds.Distinct().Count())
            throw new InvalidOperationException("The sealed form publication presentation is missing.");
        return versions;
    }

}
