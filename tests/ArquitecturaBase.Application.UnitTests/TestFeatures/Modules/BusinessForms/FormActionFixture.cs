using ArquitecturaBase.Application.Modules.BusinessForms.Interfaces.Persistence;
using ArquitecturaBase.Application.Modules.BusinessForms.Interfaces.Services;
using ArquitecturaBase.Application.Modules.BusinessForms.Models;
using ArquitecturaBase.Application.Modules.BusinessForms.Services;
using ArquitecturaBase.Domain.Modules.BusinessForms;
using ArquitecturaBase.Domain.Results;
using Microsoft.Extensions.Time.Testing;
namespace ArquitecturaBase.Application.UnitTests.TestFeatures.Modules.BusinessForms;
internal sealed class FormActionFixture : IFormPdfDesignReader
{
    public FormRuleFixture Rules { get; }
    public FormPdfDesignStoredRow? CurrentDesign { get; set; }
    public MemoryFormActionRuns Runs { get; }=new();
    public MemoryFormActionRecipients Recipients { get; }=new();
    public FakeFormPdfActionService Pdf { get; }=new();
    public FakeTimeProvider Clock { get; }=new(new DateTimeOffset(2026,10,16,12,0,0,TimeSpan.Zero));
    public FormVersionSchema Schema { get; }
    public FormActionRunRecorder Recorder { get; }
    public FormActionFixture(string originalCulture = "es")
    {
        Rules = new(originalCulture);
        Schema=new(1,[new(Rules.FieldId,"visit","longText",new Dictionary<string,string>{{"es","Visita"},{"en","Visit"}},null,false,10000,null)]);
        Recorder=new(Rules,Runs,Recipients,Pdf,this,Clock);
    }
    public (BusinessFormRecord Record,FormActionRecordSnapshot Snapshot) Capture(IReadOnlyList<FormRule> rules,string answer="Answer")
    {
        var ruleSet=BusinessFormRuleSet.Create(Rules.Definition.Id,1,Rules.Definition.Culture,FormDraftPolicy.Serialize(rules),rules.SelectMany(rule=>rule.Actions).Any(action=>action.Kind=="RequestApproval"),FormDraftPolicy.Serialize(Schema));
        Rules.RuleSets.Add(ruleSet);Rules.Definition.SetActiveRuleSetId(ruleSet.Id);
        var values=FormDraftPolicy.Serialize(new[]{new{fieldId=Rules.FieldId,value=answer}});
        var record=BusinessFormRecord.Create(Rules.Definition.Id,Guid.NewGuid(),1,Rules.User.UserId!.Value,Guid.NewGuid(),Clock.GetUtcNow().UtcDateTime,values,ruleSet.Id);
        Runs.Records.Add(record);Recipients.People[record.ActorUserId]=new(record.ActorUserId,"en");
        var stored=new FormRecordStoredRow(record.SourceReceiptId,record.Id,1,record.DefinitionId,"VIS",record.VersionId,1,
            "{\"es\":\"Visitas\",\"en\":\"Visits\"}",FormDraftPolicy.Serialize(Schema),values);
        return(record,new(new(stored,record.SubmittedAtUtc,new(record.ActorUserId,"Author")),Rules.Definition.Culture));
    }
    public static FormRuleAction Action(string kind="NotifyAuthor",bool includePdf=false)=>new(Guid.NewGuid(),kind,null,kind is "NotifyAuthor" or "NotifyRole"
        ?new Dictionary<string,string>{{"es","Recibimos {número}: {Visita}"},{"en","Received {number}: {Visit}"}}:null,includePdf);
    public Task<FormPdfDesignStoredRow?> FindCurrentAsync(Guid definitionId, CancellationToken ct) =>
        Task.FromResult(CurrentDesign?.DefinitionId == definitionId ? CurrentDesign : null);
    public Task<FormRuleDefinitionRow?> FindDefinitionAsync(Guid definitionId, CancellationToken ct) => throw new NotSupportedException();
    public Task<FormPdfDesignStoredRow?> FindRevisionAsync(Guid definitionId, long revision, CancellationToken ct) => throw new NotSupportedException();
    public Task<FormPdfDocumentRow?> FindDocumentAsync(Guid recordId, Guid documentId, CancellationToken ct) => throw new NotSupportedException();
}
internal sealed class MemoryFormActionRuns:IFormActionRunRepository,IFormActionRunReader
{
    public List<BusinessFormActionRun> Runs { get; }=[];public List<BusinessFormTask> Tasks { get; }=[];public List<BusinessFormRecord> Records { get; }=[];
    public Action? BeforeWorkflowReturn { get; set; }
    public Task<BusinessFormRecord?> GetRecordAsync(Guid id,CancellationToken ct)=>Task.FromResult(Records.SingleOrDefault(record=>record.Id==id));
    public Task<BusinessFormActionRun?> GetAsync(Guid id,CancellationToken ct)=>Task.FromResult(Runs.SingleOrDefault(run=>run.Id==id));
    public Task<IReadOnlyList<BusinessFormActionRun>> ListForEventAsync(Guid recordId,string eventName,CancellationToken ct)=>Task.FromResult<IReadOnlyList<BusinessFormActionRun>>(Runs.Where(run=>run.RecordId==recordId&&run.Event==eventName).ToArray());
    public Task<BusinessFormTask?> GetOpenTaskAsync(Guid id,CancellationToken ct)=>Task.FromResult(Tasks.SingleOrDefault(task=>task.RecordId==id&&task.State==FormTaskState.Open));
    public void Add(BusinessFormActionRun run)=>Runs.Add(run);public void AddTask(BusinessFormTask task)=>Tasks.Add(task);
    public Task<IReadOnlyList<Guid>> ListExistingPdfRunIdsAsync(IReadOnlyList<Guid> ids,CancellationToken ct)=>Task.FromResult<IReadOnlyList<Guid>>(
        Runs.Where(run=>ids.Contains(run.Id)&&run.Kind=="GeneratePdf"&&Records.Any(record=>record.Id==run.RecordId)).Select(run=>run.Id).ToArray());
    public Task<IReadOnlyList<Guid>> ListDueIdsAsync(DateTime nowUtc,CancellationToken ct)=>Task.FromResult<IReadOnlyList<Guid>>(Runs.Select(run=>run.Id).ToArray());
    public Task<FormWorkflowResponse?> FindWorkflowAsync(Guid id,CancellationToken ct)
    {
        BeforeWorkflowReturn?.Invoke();var record=Records.SingleOrDefault(record=>record.Id==id);
        return Task.FromResult(record is null?null:new FormWorkflowResponse(id,record.State.ToString(),1,null,
            Runs.Where(run=>run.RecordId==id).Select(run=>new FormActionRunResponse(run.Id,run.RuleId,run.Kind,run.Event,run.Order,run.State.ToString(),run.FailureCode,null,
                run.State==FormActionRunState.Uncertain,run.CanRetry||run.State==FormActionRunState.Uncertain,run.AcceptedAtUtc,run.UpdatedAtUtc,run.CompletedAtUtc)).ToArray(),[]));
    }
}
internal sealed class MemoryFormActionRecipients:IFormActionRecipientReader
{
    public Dictionary<Guid,FormActionRecipientRow> People { get; }=[];
    public Task<FormActionRecipientRow?> FindAsync(Guid id,CancellationToken ct)=>Task.FromResult(People.GetValueOrDefault(id));
    public Task<IReadOnlyList<FormActionRecipientRow>> ListForRoleAsync(Guid roleId,CancellationToken ct)=>Task.FromResult<IReadOnlyList<FormActionRecipientRow>>(People.Values.ToArray());
}
internal sealed class FakeFormPdfActionService:IFormPdfActionService
{
    public int Prepared { get; private set; }
    public IReadOnlyList<string> Cultures { get; set; }=["es","en"];
    public List<bool> Executed { get; }=[];public Queue<FormPdfActionOutcomeResponse> Outcomes { get; }=[];
    public Task<Result<FormPdfActionSnapshotResponse>> PrepareAsync(Guid runId,FormActionRecordSnapshot record,CancellationToken ct)
    {
        Prepared++;return Task.FromResult(Result.Success(new FormPdfActionSnapshotResponse(runId,null,0,new(new Dictionary<string,string>{{"es","PDF"},{"en","PDF"}},[],true,null,true),record,Cultures)));
    }
    public Task<Result<FormPdfActionOutcomeResponse>> ExecuteAsync(FormPdfActionSnapshotResponse snapshot,bool reconcile,CancellationToken ct)
    {
        Executed.Add(reconcile);return Task.FromResult(Result.Success(Outcomes.Count>0?Outcomes.Dequeue():new FormPdfActionOutcomeResponse(FormPdfActionState.Completed,[])));
    }
}
