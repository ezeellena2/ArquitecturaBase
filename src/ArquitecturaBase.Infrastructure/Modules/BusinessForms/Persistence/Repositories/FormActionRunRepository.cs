using ArquitecturaBase.Application.Modules.BusinessForms.Interfaces.Persistence;
using ArquitecturaBase.Domain.Modules.BusinessForms;
using ArquitecturaBase.Infrastructure.Persistence;
using ArquitecturaBase.Infrastructure.Persistence.Extensions;
using Microsoft.EntityFrameworkCore;
namespace ArquitecturaBase.Infrastructure.Modules.BusinessForms.Persistence.Repositories;
internal sealed class FormActionRunRepository(ApplicationDbContext db):IFormActionRunRepository
{
    public Task<BusinessFormRecord?> GetRecordAsync(Guid recordId,CancellationToken cancellationToken)=>db.Set<BusinessFormRecord>().SingleOrDefaultAsync(record=>record.Id==recordId,cancellationToken);
    public Task<BusinessFormActionRun?> GetAsync(Guid id,CancellationToken cancellationToken)=>db.Set<BusinessFormActionRun>().SingleOrDefaultAsync(run=>run.Id==id,cancellationToken);
    public async Task<IReadOnlyList<BusinessFormActionRun>> ListForEventAsync(Guid recordId,string eventName,CancellationToken cancellationToken)=>
        await db.Set<BusinessFormActionRun>().Where(run=>run.RecordId==recordId&&run.Event==eventName).ToArrayAsync(cancellationToken);
    public Task<BusinessFormTask?> GetOpenTaskAsync(Guid recordId,CancellationToken cancellationToken)=>db.Set<BusinessFormTask>().SingleOrDefaultAsync(task=>task.RecordId==recordId&&task.State==FormTaskState.Open,cancellationToken);
    public void Add(BusinessFormActionRun run){db.RequireTransaction();db.Set<BusinessFormActionRun>().Add(run);}
    public void AddTask(BusinessFormTask task){db.RequireTransaction();db.Set<BusinessFormTask>().Add(task);}
}
