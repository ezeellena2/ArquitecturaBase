using ArquitecturaBase.Application.Modules.BusinessForms.Interfaces.Persistence;
using ArquitecturaBase.Application.Modules.BusinessForms.Models;
using ArquitecturaBase.Domain.Modules.BusinessForms;
using ArquitecturaBase.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace ArquitecturaBase.Infrastructure.Modules.BusinessForms.Persistence.Readers;

internal sealed class FormRecordResultReader(ApplicationDbContext db) : IFormRecordResultReader
{
    public async Task<IReadOnlyList<FormRecordResultRow>> ListAsync(IReadOnlyList<Guid> recordIds, CancellationToken cancellationToken) =>
        recordIds.Count == 0 ? [] : await db.Set<BusinessFormRecordResult>().AsNoTracking().Where(result => recordIds.Contains(result.RecordId))
            .OrderBy(result => result.RecordId).ThenBy(result => result.RecordedAtUtc).ThenBy(result => result.Id)
            .Select(result => new FormRecordResultRow { RecordId = result.RecordId, RunId = result.RunId, Source = result.Source,
                SourceNameJson = result.SourceNameJson, ValuesJson = result.ValuesJson, RecordedAtUtc = result.RecordedAtUtc })
            .ToArrayAsync(cancellationToken);

    // The sources of the form, and for each one its latest result through a lateral subquery (EF does not translate a grouped First).
    public async Task<IReadOnlyList<FormRecordResultRow>> ListLatestBySourceAsync(Guid definitionId, CancellationToken cancellationToken)
    {
        var results = db.Set<BusinessFormRecordResult>().AsNoTracking().Where(result => result.DefinitionId == definitionId);
        return await results.Select(result => result.Source).Distinct().OrderBy(source => source)
            .Select(source => results.Where(result => result.Source == source)
                .OrderByDescending(result => result.RecordedAtUtc).ThenByDescending(result => result.Id)
                .Select(result => new FormRecordResultRow { RecordId = result.RecordId, RunId = result.RunId, Source = result.Source,
                    SourceNameJson = result.SourceNameJson, ValuesJson = result.ValuesJson, RecordedAtUtc = result.RecordedAtUtc })
                .First())
            .ToArrayAsync(cancellationToken);
    }
}
