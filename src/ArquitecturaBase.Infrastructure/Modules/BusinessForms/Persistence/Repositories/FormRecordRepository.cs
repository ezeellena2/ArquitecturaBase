using ArquitecturaBase.Application.Modules.BusinessForms.Interfaces.Persistence;
using ArquitecturaBase.Domain.Modules.BusinessForms;
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
    public void Add(BusinessFormRecord record) { dbContext.RequireTransaction(); dbContext.Set<BusinessFormRecord>().Add(record); }
    public void Add(BusinessFormRecordValue value) { dbContext.RequireTransaction(); dbContext.Set<BusinessFormRecordValue>().Add(value); }
    public void Add(BusinessFormCaptureReceipt receipt) { dbContext.RequireTransaction(); dbContext.Set<BusinessFormCaptureReceipt>().Add(receipt); }
}
