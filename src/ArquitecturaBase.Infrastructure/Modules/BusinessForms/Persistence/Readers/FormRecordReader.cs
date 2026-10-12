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
        var rows = await (from receipt in dbContext.Set<BusinessFormCaptureReceipt>().AsNoTracking()
                      where sourceReceiptIds.Contains(receipt.SourceReceiptId) && receipt.State == "Completed" && receipt.RecordId != null
                      join record in dbContext.Set<BusinessFormRecord>().AsNoTracking() on receipt.RecordId equals (Guid?)record.Id
                      join definition in dbContext.Set<BusinessFormDefinition>().AsNoTracking() on record.DefinitionId equals definition.Id
                      join version in dbContext.Set<BusinessFormVersion>().AsNoTracking() on record.VersionId equals version.Id
                      select new FormRecordStoredRow(receipt.SourceReceiptId, record.Id, record.Number, definition.Id,
                          definition.Prefix, version.Id, version.Number, version.NameJson!, version.SchemaJson, record.ValuesJson))
            .ToListAsync(cancellationToken);
        var ids = rows.Select(row => row.RecordId).ToArray();
        var references = await dbContext.Set<BusinessFormRecordReference>().AsNoTracking()
            .Where(reference => ids.Contains(reference.RecordId))
            .Select(reference => new FormRecordReferenceRow(reference.RecordId, reference.FieldId, reference.CatalogId,
                reference.CatalogRevision, reference.ItemId, reference.Text, reference.Detail, reference.CatalogName))
            .ToListAsync(cancellationToken);
        var byRecord = references.ToLookup(reference => reference.RecordId);
        return rows.Select(row => row with { References = byRecord[row.RecordId].ToArray() }).ToArray();
    }
}
