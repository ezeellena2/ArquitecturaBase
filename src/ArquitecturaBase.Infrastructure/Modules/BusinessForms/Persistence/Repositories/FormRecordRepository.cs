using ArquitecturaBase.Application.Modules.BusinessForms.Interfaces.Persistence;
using ArquitecturaBase.Domain.Modules.BusinessForms;
using ArquitecturaBase.Application.Modules.BusinessForms.Models;
using ArquitecturaBase.Infrastructure.Persistence;
using ArquitecturaBase.Infrastructure.Persistence.Extensions;
using Microsoft.EntityFrameworkCore;

namespace ArquitecturaBase.Infrastructure.Modules.BusinessForms.Persistence.Repositories;

internal sealed class FormRecordRepository(ApplicationDbContext dbContext) : IFormRecordRepository
{
    public Task LockSourceReceiptAsync(Guid sourceReceiptId, CancellationToken cancellationToken) =>
        dbContext.AcquireAdvisoryLocksAsync([FormAdvisoryLockKeys.Capture(sourceReceiptId)], cancellationToken);
    public Task LockDefinitionsAsync(CancellationToken cancellationToken) =>
        dbContext.AcquireAdvisoryLocksAsync([FormAdvisoryLockKeys.Definitions], cancellationToken);
    public Task<BusinessFormCaptureReceipt?> GetReceiptAsync(Guid sourceReceiptId, CancellationToken cancellationToken) =>
        dbContext.Set<BusinessFormCaptureReceipt>().SingleOrDefaultAsync(value => value.SourceReceiptId == sourceReceiptId, cancellationToken);
    public Task<BusinessFormDefinition?> GetDefinitionAsync(Guid id, CancellationToken cancellationToken) =>
        dbContext.Set<BusinessFormDefinition>().SingleOrDefaultAsync(value => value.Id == id, cancellationToken);
    public Task<BusinessFormVersion?> GetVersionAsync(Guid id, CancellationToken cancellationToken) =>
        dbContext.Set<BusinessFormVersion>().SingleOrDefaultAsync(value => value.Id == id, cancellationToken);
    public Task<bool> ExistsPublishedAsync(Guid versionId, CancellationToken cancellationToken) =>
        dbContext.Set<BusinessFormPublicationOperation>().AnyAsync(value => value.VersionId == versionId
            && value.State == FormPublicationState.Published, cancellationToken);
    public Task LockCatalogAsync(Guid catalogId, CancellationToken cancellationToken) =>
        dbContext.AcquireAdvisoryLocksAsync([FormAdvisoryLockKeys.Catalog(catalogId)], cancellationToken);
    public Task<bool> ExistsActiveCatalogItemAsync(Guid catalogId, Guid itemId, CancellationToken cancellationToken) =>
        dbContext.Set<BusinessFormCatalogItem>().AnyAsync(item => item.CatalogId == catalogId && item.Id == itemId && item.IsActive, cancellationToken);
    public async Task<IReadOnlyList<FormCatalogItemFieldValuesRow>> ListCatalogFieldValuesAsync(IReadOnlyCollection<Guid> itemIds, CancellationToken cancellationToken) =>
        await (from item in dbContext.Set<BusinessFormCatalogItem>()
               join catalog in dbContext.Set<BusinessFormCatalog>() on item.CatalogId equals catalog.Id
               where itemIds.Contains(item.Id)
               select new FormCatalogItemFieldValuesRow(item.Id, item.CatalogId, catalog.FieldsJson, item.FieldValuesJson,
                   item.TextEs, item.TextEn)).ToListAsync(cancellationToken);
    public void Add(BusinessFormRecordFile file) { dbContext.RequireTransaction(); dbContext.Set<BusinessFormRecordFile>().Add(file); }
    public void Add(BusinessFormRecordReference reference) { dbContext.RequireTransaction(); dbContext.Set<BusinessFormRecordReference>().Add(reference); }
    public void Add(BusinessFormRecord record) { dbContext.RequireTransaction(); dbContext.Set<BusinessFormRecord>().Add(record); }
    public void Add(BusinessFormRecordValue value) { dbContext.RequireTransaction(); dbContext.Set<BusinessFormRecordValue>().Add(value); }
    public void Add(BusinessFormCaptureReceipt receipt) { dbContext.RequireTransaction(); dbContext.Set<BusinessFormCaptureReceipt>().Add(receipt); }
}
