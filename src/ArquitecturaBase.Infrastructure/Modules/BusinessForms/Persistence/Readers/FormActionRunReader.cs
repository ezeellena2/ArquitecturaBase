using ArquitecturaBase.Application.Modules.BusinessForms.Interfaces.Persistence;
using ArquitecturaBase.Application.Modules.BusinessForms.Models;
using ArquitecturaBase.Domain.Files;
using ArquitecturaBase.Domain.Modules.BusinessForms;
using ArquitecturaBase.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
namespace ArquitecturaBase.Infrastructure.Modules.BusinessForms.Persistence.Readers;
internal sealed class FormActionRunReader(ApplicationDbContext db,TimeProvider timeProvider):IFormActionRunReader
{
    public async Task<IReadOnlyList<Guid>> ListExistingPdfRunIdsAsync(IReadOnlyList<Guid> runIds, CancellationToken cancellationToken) =>
        await db.Set<BusinessFormActionRun>().AsNoTracking().Where(run => runIds.Contains(run.Id) && run.Kind == "GeneratePdf"
            && db.Set<BusinessFormRecord>().Any(record => record.Id == run.RecordId)).Select(run => run.Id).ToArrayAsync(cancellationToken);
    public async Task<IReadOnlyList<Guid>> ListDueIdsAsync(DateTime nowUtc,CancellationToken cancellationToken)=>await db.Set<BusinessFormActionRun>().AsNoTracking()
        .Where(run=>(run.State==FormActionRunState.Waiting||run.State==FormActionRunState.Running)
            &&(run.LeaseExpiresAtUtc==null||run.LeaseExpiresAtUtc<=nowUtc)&&(run.NextCheckAtUtc==null||run.NextCheckAtUtc<=nowUtc)
            &&(run.DependsOnRunId==null||db.Set<BusinessFormActionRun>().Any(previous=>previous.Id==run.DependsOnRunId
                &&(previous.State==FormActionRunState.Completed||previous.State==FormActionRunState.NotApplicable))))
        .OrderBy(run=>run.NextCheckAtUtc??run.AcceptedAtUtc).ThenBy(run=>run.AcceptedAtUtc).ThenBy(run=>run.Id).Select(run=>run.Id).Take(100).ToArrayAsync(cancellationToken);
    public async Task<FormWorkflowResponse?> FindWorkflowAsync(Guid recordId,CancellationToken cancellationToken)
    {
        var record=await db.Set<BusinessFormRecord>().AsNoTracking().Where(record=>record.Id==recordId).Select(record=>new{record.Id,record.State,
            Revision=db.Set<BusinessFormRuleSet>().Where(rules=>rules.Id==record.RuleSetId).Select(rules=>(long?)rules.Revision).SingleOrDefault()}).SingleOrDefaultAsync(cancellationToken);
        if(record is null)return null;var now=timeProvider.GetUtcNow().UtcDateTime;
        var task=await db.Set<BusinessFormTask>().AsNoTracking().Where(task=>task.RecordId==recordId)
            .Select(task=>new FormWorkflowTaskResponse(task.Id,task.RoleId,task.Revision,task.State.ToString(),
                db.Roles.Where(role=>role.Id==task.RoleId).Select(role=>role.Name).FirstOrDefault(),task.DecidedAtUtc,
                task.DecidedByUserId==null?null:new FormAuthorResponse(task.DecidedByUserId.Value,
                    db.Users.Where(user=>user.Id==task.DecidedByUserId).Select(user=>user.DisplayName).FirstOrDefault()),task.Reason)).SingleOrDefaultAsync(cancellationToken);
        var actions=await db.Set<BusinessFormActionRun>().AsNoTracking().Where(run=>run.RecordId==recordId).OrderBy(run=>run.AcceptedAtUtc)
            .ThenBy(run=>run.Event=="Received"?0:1).ThenBy(run=>run.RuleId).ThenBy(run=>run.Order)
            .Select(run=>new FormActionRunResponse(run.Id,run.RuleId,run.Kind,run.Event,run.Order,run.State.ToString(),run.FailureCode,null,
                run.State==FormActionRunState.Uncertain||run.State==FormActionRunState.Running&&run.LeaseExpiresAtUtc<=now,
                run.CanRetry||run.State==FormActionRunState.Uncertain,run.AcceptedAtUtc,run.UpdatedAtUtc,run.CompletedAtUtc)).ToArrayAsync(cancellationToken);
        var documents=await (from document in db.Set<BusinessFormDocument>().AsNoTracking() where document.RecordId==recordId
            join file in db.Set<StoredFile>().AsNoTracking() on document.FileId equals file.Id
            orderby document.CreatedAtUtc,document.Id select new FormWorkflowDocumentResponse(document.Id,document.Culture,file.Name,file.Size,document.CreatedAtUtc)).ToArrayAsync(cancellationToken);
        return new(record.Id,record.State.ToString(),record.Revision,task,actions,documents);
    }
}
