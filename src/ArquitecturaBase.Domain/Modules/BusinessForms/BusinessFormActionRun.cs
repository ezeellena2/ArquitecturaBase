using ArquitecturaBase.Domain.Common;
using ArquitecturaBase.Domain.Results;
namespace ArquitecturaBase.Domain.Modules.BusinessForms;
public enum FormActionRunState { Waiting, Running, Completed, Failed, Uncertain, NotApplicable }
public sealed record FormActionReservation(long Fence,bool ReconciliationOnly);
public sealed class BusinessFormActionRun : Entity, IAuditable
{
    private BusinessFormActionRun() { }
    public Guid DefinitionId { get; private set; }
    public Guid RecordId { get; private set; }
    public Guid RuleSetId { get; private set; }
    public Guid RuleId { get; private set; }
    public Guid ActionId { get; private set; }
    public string Event { get; private set; } = "";
    public string Kind { get; private set; } = "";
    public int Order { get; private set; }
    public Guid? DependsOnRunId { get; private set; }
    public string SnapshotJson { get; private set; } = "{}";
    public string? OutcomeJson { get; private set; }
    public FormActionRunState State { get; private set; }
    public string? FailureCode { get; private set; }
    public bool CanRetry { get; private set; }
    public bool RetryRequested { get; private set; }
    public long Fence { get; private set; }
    public DateTime? LeaseExpiresAtUtc { get; private set; }
    public DateTime? NextCheckAtUtc { get; private set; }
    public DateTime AcceptedAtUtc { get; private set; }
    public DateTime UpdatedAtUtc { get; private set; }
    public DateTime? CompletedAtUtc { get; private set; }
    public DateTime CreatedAtUtc { get; private set; }
    public Guid? CreatedBy { get; private set; }
    public DateTime? ModifiedAtUtc { get; private set; }
    public Guid? ModifiedBy { get; private set; }
    public static BusinessFormActionRun Create(Guid definitionId,Guid recordId,Guid ruleSetId,Guid ruleId,Guid actionId,
        string eventName,string kind,int order,Guid? dependency,string snapshotJson,bool applies,DateTime nowUtc) => new()
    {
        DefinitionId=definitionId,RecordId=recordId,RuleSetId=ruleSetId,RuleId=ruleId,ActionId=actionId,Event=eventName,Kind=kind,Order=order,
        DependsOnRunId=dependency,SnapshotJson=snapshotJson,State=applies?FormActionRunState.Waiting:FormActionRunState.NotApplicable,
        AcceptedAtUtc=nowUtc,UpdatedAtUtc=nowUtc,CompletedAtUtc=applies?null:nowUtc,
    };
    public void SealSnapshot(string snapshotJson)
    {
        if(SnapshotJson.Length!=0)throw new InvalidOperationException("An action snapshot is immutable.");
        ArgumentException.ThrowIfNullOrWhiteSpace(snapshotJson);SnapshotJson=snapshotJson;
    }
    public Result<FormActionReservation> Reserve(DateTime nowUtc,TimeSpan lease)
    {
        if(State is FormActionRunState.Completed or FormActionRunState.Failed or FormActionRunState.NotApplicable
            || LeaseExpiresAtUtc>nowUtc || NextCheckAtUtc>nowUtc || lease<=TimeSpan.Zero || Fence==long.MaxValue) return FormActionErrors.StateConflict;
        var reconcile=State is FormActionRunState.Running or FormActionRunState.Uncertain;
        Fence++;State=FormActionRunState.Running;LeaseExpiresAtUtc=nowUtc.Add(lease);NextCheckAtUtc=null;UpdatedAtUtc=nowUtc;
        return new FormActionReservation(Fence,reconcile);
    }
    public Result RecordOutcome(long fence,DateTime nowUtc,FormActionRunState state,string? failureCode,bool canRetry,string? outcomeJson=null)
    {
        if(!OwnsLease(fence,nowUtc) || state is not (FormActionRunState.Running or FormActionRunState.Completed or FormActionRunState.Failed or FormActionRunState.Uncertain)) return FormActionErrors.StateConflict;
        State=state;FailureCode=failureCode;CanRetry=state==FormActionRunState.Failed&&canRetry;OutcomeJson=outcomeJson;UpdatedAtUtc=nowUtc;
        LeaseExpiresAtUtc=null;NextCheckAtUtc=state is FormActionRunState.Running or FormActionRunState.Uncertain?nowUtc.AddSeconds(5):null;
        CompletedAtUtc=state is FormActionRunState.Completed or FormActionRunState.Failed?nowUtc:null;RetryRequested=false;return Result.Success();
    }
    public bool OwnsLease(long fence,DateTime nowUtc)=>Fence==fence&&State==FormActionRunState.Running&&LeaseExpiresAtUtc>nowUtc;
    public Result RequestRetry(DateTime nowUtc)
    {
        if(State is FormActionRunState.Completed or FormActionRunState.NotApplicable) return Result.Success();
        if(State==FormActionRunState.Failed)
        {
            if(!CanRetry) return FormActionErrors.StateConflict;
            State=FormActionRunState.Waiting;FailureCode=null;CompletedAtUtc=null;CanRetry=false;
        }
        else if(State==FormActionRunState.Running&&LeaseExpiresAtUtc>nowUtc) return Result.Success();
        RetryRequested=true;NextCheckAtUtc=null;UpdatedAtUtc=nowUtc;return Result.Success();
    }
    public Result RequestConsult(DateTime nowUtc)
    {
        if(State is FormActionRunState.Completed or FormActionRunState.NotApplicable) return Result.Success();
        if(State!=FormActionRunState.Uncertain && !(State==FormActionRunState.Running&&LeaseExpiresAtUtc<=nowUtc)) return FormActionErrors.StateConflict;
        RetryRequested=false;NextCheckAtUtc=null;UpdatedAtUtc=nowUtc;return Result.Success();
    }
    public override string ToString()=>$"BusinessFormActionRun {{ Id = {Id}, State = {State}, Content = [REDACTED] }}";
}
