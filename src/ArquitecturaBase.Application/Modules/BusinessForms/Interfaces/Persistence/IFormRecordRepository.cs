using ArquitecturaBase.Domain.Modules.BusinessForms;
using ArquitecturaBase.Application.Modules.BusinessForms.Models;

namespace ArquitecturaBase.Application.Modules.BusinessForms.Interfaces.Persistence;

public interface IFormRecordRepository
{
    Task LockSourceReceiptAsync(Guid sourceReceiptId, CancellationToken cancellationToken);
    Task LockDefinitionsAsync(CancellationToken cancellationToken);
    Task<BusinessFormCaptureReceipt?> GetReceiptAsync(Guid sourceReceiptId, CancellationToken cancellationToken);
    Task<BusinessFormDefinition?> GetDefinitionAsync(Guid id, CancellationToken cancellationToken);
    Task<BusinessFormVersion?> GetVersionAsync(Guid id, CancellationToken cancellationToken);
    Task<bool> ExistsPublishedAsync(Guid versionId, CancellationToken cancellationToken);
    Task LockCatalogAsync(Guid catalogId, CancellationToken cancellationToken);
    Task<bool> ExistsActiveCatalogItemAsync(Guid catalogId, Guid itemId, CancellationToken cancellationToken);
    Task<IReadOnlyList<FormCatalogItemFieldValuesRow>> FindCatalogFieldValuesAsync(IReadOnlyCollection<Guid> itemIds, CancellationToken cancellationToken);
    void Add(BusinessFormRecordFile file);
    void Add(BusinessFormRecordReference reference);
    void Add(BusinessFormRecord record);
    void Add(BusinessFormRecordValue value);
    void Add(BusinessFormCaptureReceipt receipt);
}
