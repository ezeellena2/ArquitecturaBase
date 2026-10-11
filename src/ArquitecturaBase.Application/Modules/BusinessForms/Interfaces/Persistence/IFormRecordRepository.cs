using ArquitecturaBase.Domain.Modules.BusinessForms;

namespace ArquitecturaBase.Application.Modules.BusinessForms.Interfaces.Persistence;

public interface IFormRecordRepository
{
    Task LockSourceReceiptAsync(Guid sourceReceiptId, CancellationToken cancellationToken);
    Task LockDefinitionsAsync(CancellationToken cancellationToken);
    Task<BusinessFormCaptureReceipt?> GetReceiptAsync(Guid sourceReceiptId, CancellationToken cancellationToken);
    Task<BusinessFormDefinition?> GetDefinitionAsync(Guid id, CancellationToken cancellationToken);
    Task<BusinessFormVersion?> GetVersionAsync(Guid id, CancellationToken cancellationToken);
    Task<bool> ExistsPublishedAsync(Guid versionId, CancellationToken cancellationToken);
    void Add(BusinessFormRecord record);
    void Add(BusinessFormRecordValue value);
    void Add(BusinessFormCaptureReceipt receipt);
}
