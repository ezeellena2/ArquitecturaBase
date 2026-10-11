using ArquitecturaBase.Application.Modules.BusinessForms.Models;

namespace ArquitecturaBase.Application.Modules.BusinessForms.Interfaces.Persistence;

public interface IFormRecordReader
{
    Task<IReadOnlyList<FormRecordStoredRow>> ListAsync(IReadOnlyCollection<Guid> sourceReceiptIds, CancellationToken cancellationToken);
}
