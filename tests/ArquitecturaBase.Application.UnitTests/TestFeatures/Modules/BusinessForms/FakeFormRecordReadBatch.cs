using ArquitecturaBase.Application.Modules.BusinessForms.Interfaces.Persistence;
using ArquitecturaBase.Application.Modules.BusinessForms.Models;

namespace ArquitecturaBase.Application.UnitTests.TestFeatures.Modules.BusinessForms;

internal sealed class FakeFormRecordReadBatch : IFormRecordReader
{
    public FormRecordReadBatch Batch { get; set; } = new([], new Dictionary<Guid, FormResponseVersionRow>());
    public int Reads { get; private set; }
    public Task<IReadOnlyDictionary<Guid, FormResponseVersionRow>> ListVersionsAsync(IReadOnlyCollection<Guid> ids, CancellationToken ct) =>
        Task.FromResult(Batch.Versions);
    public Task<FormRecordReadBatch> FindBatchAsync(IReadOnlyCollection<Guid> ids, IReadOnlyDictionary<Guid, FormResponseVersionRow>? versions, CancellationToken ct)
    {
        Reads++;
        return Task.FromResult(Batch);
    }
}
