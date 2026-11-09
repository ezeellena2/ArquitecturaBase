using ArquitecturaBase.Application.Modules.BusinessForms.Interfaces.Persistence;
using ArquitecturaBase.Application.Modules.BusinessForms.Models;

namespace ArquitecturaBase.Application.UnitTests.TestFeatures.Modules.BusinessForms;

/// <summary>The results of external actions (RE1-A) a test arranges; none by default.</summary>
internal sealed class FormRecordResultReaderDouble : IFormRecordResultReader
{
    public List<FormRecordResultRow> Rows { get; } = [];
    public Task<IReadOnlyList<FormRecordResultRow>> ListAsync(IReadOnlyList<Guid> recordIds, CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<FormRecordResultRow>>(Rows.Where(row => recordIds.Contains(row.RecordId)).ToArray());
    public Task<IReadOnlyList<FormRecordResultRow>> ListLatestBySourceAsync(Guid definitionId, CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<FormRecordResultRow>>(Rows.GroupBy(row => row.Source).Select(group => group.OrderByDescending(row => row.RecordedAtUtc).First()).ToArray());
}
