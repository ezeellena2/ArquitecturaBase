using ArquitecturaBase.Application.Modules.BusinessForms.Models;

namespace ArquitecturaBase.Application.Modules.BusinessForms.Interfaces.Persistence;

public interface IFormRecordReader
{
    Task<IReadOnlyDictionary<Guid, FormResponseVersionRow>> ListVersionsAsync(IReadOnlyCollection<Guid> versionIds, CancellationToken cancellationToken);
    Task<FormRecordReadBatch> FindBatchAsync(IReadOnlyCollection<Guid> sourceReceiptIds, IReadOnlyDictionary<Guid, FormResponseVersionRow>? versions, CancellationToken cancellationToken);
}
