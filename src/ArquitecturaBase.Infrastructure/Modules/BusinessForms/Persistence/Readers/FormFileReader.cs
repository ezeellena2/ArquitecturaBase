using ArquitecturaBase.Application.Modules.BusinessForms.Interfaces.Persistence;
using ArquitecturaBase.Application.Modules.BusinessForms.Models;
using ArquitecturaBase.Domain.Modules.BusinessForms;
using ArquitecturaBase.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
namespace ArquitecturaBase.Infrastructure.Modules.BusinessForms.Persistence.Readers;
internal sealed class FormFileReader(ApplicationDbContext dbContext) : IFormFileReader
{
    public Task<FormFileAccessRow?> FindAsync(Guid recordId, Guid fileId, CancellationToken cancellationToken) =>
        (from reference in dbContext.Set<BusinessFormRecordFile>().AsNoTracking()
         where reference.RecordId == recordId && reference.FileId == fileId
         join field in dbContext.Set<BusinessFormField>().AsNoTracking() on reference.FieldId equals field.Id
         select new FormFileAccessRow(reference.FileId, field.Type == "photo")).SingleOrDefaultAsync(cancellationToken);
}
