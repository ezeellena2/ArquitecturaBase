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

    public async Task<IReadOnlyList<FormRecordResultRow>> ListLatestBySourceAsync(Guid definitionId, CancellationToken cancellationToken) =>
        await db.Set<BusinessFormRecordResult>().AsNoTracking().Where(result => result.DefinitionId == definitionId)
            .GroupBy(result => result.Source)
            .Select(group => group.OrderByDescending(result => result.RecordedAtUtc).ThenByDescending(result => result.Id).First())
            .OrderBy(result => result.Source)
            .Select(result => new FormRecordResultRow { RecordId = result.RecordId, RunId = result.RunId, Source = result.Source,
                SourceNameJson = result.SourceNameJson, ValuesJson = result.ValuesJson, RecordedAtUtc = result.RecordedAtUtc })
            .ToArrayAsync(cancellationToken);
}
