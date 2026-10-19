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
            select new { version.Id, version.Number, version.NameJson, version.SchemaJson, version.Culture, version.DefinitionId,
                OperationId = operation.Id, Actor = operation.OperatorUserId, operation.RequestId, operation.ProtectedManifest }).ToArrayAsync(ct);
        var versions = rows.Select(row =>
        {
            var manifest = protector.ReadManifest(new(row.OperationId, row.Actor, row.RequestId, row.DefinitionId, row.Id), row.ProtectedManifest);
            var cultures = manifest.Profiles.SelectMany(profile => profile.Cultures).Select(culture => culture.Culture).Distinct(StringComparer.Ordinal).ToArray();
            if (row.Culture is not ("es" or "en") || manifest.OriginalCulture != row.Culture
                || !cultures.Contains(row.Culture, StringComparer.Ordinal) || cultures.Any(culture => culture is not ("es" or "en")))
                throw new InvalidOperationException("The sealed form publication cultures are invalid.");
            return new FormResponseVersionRow(row.Id, row.Number, row.NameJson!, row.SchemaJson, row.Culture, cultures);
        }).ToDictionary(version => version.Id);
        if (versions.Count != versionIds.Distinct().Count())
            throw new InvalidOperationException("The sealed form publication presentation is missing.");
        return versions;
    }

}
