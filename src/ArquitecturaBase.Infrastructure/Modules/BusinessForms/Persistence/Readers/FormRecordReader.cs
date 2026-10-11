using ArquitecturaBase.Application.Modules.BusinessForms.Interfaces.Persistence;
using ArquitecturaBase.Application.Modules.BusinessForms.Models;
using ArquitecturaBase.Domain.Modules.BusinessForms;
using ArquitecturaBase.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace ArquitecturaBase.Infrastructure.Modules.BusinessForms.Persistence.Readers;

internal sealed class FormRecordReader(ApplicationDbContext dbContext) : IFormRecordReader
{
    public async Task<IReadOnlyList<FormRecordStoredRow>> ListAsync(IReadOnlyCollection<Guid> sourceReceiptIds, CancellationToken cancellationToken)
    {
        if (sourceReceiptIds.Count == 0) return [];
        return await (from receipt in dbContext.Set<BusinessFormCaptureReceipt>().AsNoTracking()
                      where sourceReceiptIds.Contains(receipt.SourceReceiptId) && receipt.State == "Completed" && receipt.RecordId != null
                      join record in dbContext.Set<BusinessFormRecord>().AsNoTracking() on receipt.RecordId equals (Guid?)record.Id
                      join definition in dbContext.Set<BusinessFormDefinition>().AsNoTracking() on record.DefinitionId equals definition.Id
                      join version in dbContext.Set<BusinessFormVersion>().AsNoTracking() on record.VersionId equals version.Id
                      select new FormRecordStoredRow(receipt.SourceReceiptId, record.Id, record.Number, definition.Id,
                          definition.Prefix, version.Id, version.Number, version.NameJson!, version.SchemaJson, record.ValuesJson))
            .ToListAsync(cancellationToken);
    }
}
