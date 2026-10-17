using ArquitecturaBase.Application.Modules.BusinessForms.Interfaces.Persistence;
using ArquitecturaBase.Application.Modules.BusinessForms.Models;
using ArquitecturaBase.Domain.Modules.BusinessForms;
using ArquitecturaBase.Domain.Files;
using ArquitecturaBase.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace ArquitecturaBase.Infrastructure.Modules.BusinessForms.Persistence.Readers;

internal sealed class FormRecordReader(ApplicationDbContext dbContext) : IFormRecordReader
{
    public async Task<IReadOnlyList<FormRecordStoredRow>> ListAsync(IReadOnlyCollection<Guid> sourceReceiptIds, IReadOnlyDictionary<Guid, FormResponseVersionRow>? versions, CancellationToken cancellationToken)
    {
        if (sourceReceiptIds.Count == 0) return [];
        var rows = await QueryRows(sourceReceiptIds).ToListAsync(cancellationToken);
        if(rows.Count==0)return [];
        if(versions is null)
        {
            var versionIds=rows.Select(row=>row.VersionId).Distinct().ToArray();
            versions=await dbContext.Set<BusinessFormVersion>().AsNoTracking().Where(version=>versionIds.Contains(version.Id))
                .Select(version=>new FormResponseVersionRow(version.Id,version.Number,version.NameJson!,version.SchemaJson))
                .ToDictionaryAsync(version=>version.Id,cancellationToken);
        }
        var ids = rows.Select(row => row.RecordId).ToArray();
        var references = await dbContext.Set<BusinessFormRecordReference>().AsNoTracking()
            .Where(reference => ids.Contains(reference.RecordId))
            .Select(reference => new FormRecordReferenceRow(reference.RecordId, reference.FieldId, reference.CatalogId,
                reference.CatalogRevision, reference.ItemId, reference.Text, reference.Detail, reference.CatalogName, reference.TranslationsJson))
            .ToListAsync(cancellationToken);
        var byRecord = references.ToLookup(reference => reference.RecordId);
        var files = await (from reference in dbContext.Set<BusinessFormRecordFile>().AsNoTracking()
            where ids.Contains(reference.RecordId)
            join file in dbContext.Set<StoredFile>().AsNoTracking() on reference.FileId equals file.Id
            select new FormRecordFileRow(reference.RecordId, reference.FieldId, file.Id, reference.Position,
                file.Name, file.ContentType, file.Size, file.ThumbnailKey != null)).ToListAsync(cancellationToken);
        var filesByRecord = files.ToLookup(file => file.RecordId);
        return rows.Select(row=>
        {
            var version=versions[row.VersionId];
            return new FormRecordStoredRow(row.SourceReceiptId,row.RecordId,row.Number,row.DefinitionId,row.Prefix,
                version.Id,version.Number,version.NameJson,version.SchemaJson,row.ValuesJson,byRecord[row.RecordId].ToArray(),filesByRecord[row.RecordId].ToArray());
        }).ToArray();
    }
    private IQueryable<FormRecordLightRow> QueryRows(IReadOnlyCollection<Guid> sourceReceiptIds) =>
        from receipt in dbContext.Set<BusinessFormCaptureReceipt>().AsNoTracking()
        where sourceReceiptIds.Contains(receipt.SourceReceiptId)&&receipt.State=="Completed"&&receipt.RecordId!=null
        join record in dbContext.Set<BusinessFormRecord>().AsNoTracking() on receipt.RecordId equals (Guid?)record.Id
        join definition in dbContext.Set<BusinessFormDefinition>().AsNoTracking() on record.DefinitionId equals definition.Id
        select new FormRecordLightRow(receipt.SourceReceiptId,record.Id,record.Number,definition.Id,definition.Prefix,record.VersionId,record.ValuesJson);
    private sealed record FormRecordLightRow(Guid SourceReceiptId,Guid RecordId,long Number,Guid DefinitionId,string Prefix,Guid VersionId,string ValuesJson)
    { public override string ToString()=>"FormRecordLightRow { Content = [REDACTED] }"; }
}
