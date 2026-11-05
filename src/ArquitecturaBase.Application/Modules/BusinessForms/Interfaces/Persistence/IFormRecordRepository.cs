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
    Task<IReadOnlyList<FormCatalogItemFieldValuesRow>> ListCatalogFieldValuesAsync(IReadOnlyCollection<Guid> itemIds, CancellationToken cancellationToken);
    /// <summary>The record, tracked, to correct or void it under the definitions lock.</summary>
    Task<BusinessFormRecord?> GetRecordAsync(Guid recordId, CancellationToken cancellationToken);
    /// <summary>The open approval task of a record, tracked: correcting or voiding the record closes it.</summary>
    Task<BusinessFormTask?> GetOpenTaskAsync(Guid recordId, CancellationToken cancellationToken);
    /// <summary>The files of a record with their stored metadata, in order: a correction keeps the ones nobody touched.</summary>
    Task<IReadOnlyList<FormRecordFileRow>> ListFilesAsync(Guid recordId, CancellationToken cancellationToken);
    void Add(BusinessFormRecordFile file);
    void Add(BusinessFormRecordReference reference);
    void Add(BusinessFormRecord record);
    void Add(BusinessFormRecordValue value);
    void Add(BusinessFormCaptureReceipt receipt);
}
