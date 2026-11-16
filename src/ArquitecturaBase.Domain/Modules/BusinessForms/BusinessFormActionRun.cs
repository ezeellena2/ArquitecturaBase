using System.Linq.Expressions;
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
    public Guid? RetryRecipientUserId { get; private set; }
    public bool DeliveryReviewRequested { get; private set; }
    public Guid? DeliveryReviewRecipientUserId { get; private set; }
    public bool? DeliveryReviewWasSent { get; private set; }
    public long? DeliveryReviewExpectedRevision { get; private set; }
    public Guid? DeliveryReviewedByUserId { get; private set; }
    public DateTime? DeliveryReviewedAtUtc { get; private set; }
    /// <summary>H21: the channel's tap of its latest decision from a button, so that same tap handed again is answered the same.</summary>
    public Guid? DecisionTapId { get; private set; }
    /// <summary>F19: what a person last asked of this action («Retry», «RecipientRetry» or «Consult»), who and when; it stays after the result.</summary>
    public string? LastRequestKind { get; private set; }
    public Guid? LastRequestedByUserId { get; private set; }
    public DateTime? LastRequestedAtUtc { get; private set; }
    /// <summary>F2: how many times the system asked on its own, with a growing wait, about an effect left in doubt by a local error.</summary>
    public int AutoConsultCount { get; private set; }
    public const int MaxAutoConsults = 8;
    /// <summary>F2: when whoever loaded the response was told that this action stopped its rule (or found without a channel to tell them); once per run.</summary>
    public DateTime? StallNoticedAtUtc { get; private set; }
    /// <summary>F18: a W3C traceparent, the only form that fits.</summary>
    public const int TraceParentLength = 55;
    /// <summary>F18: the trace context where the run was created (the capture's request or the round of a channel), which its worker continues; none before F18 or without a trace.</summary>
    public string? TraceParent { get; private set; }
    /// <summary>F18: kept once, when the run is first saved.</summary>
    public void RecordTrace(string traceParent)
    {
        if(traceParent is not { Length: TraceParentLength })throw new ArgumentException("A trace context is a W3C traceparent.",nameof(traceParent));
        TraceParent??=traceParent;
    }
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
        if(State is FormActionRunState.Completed or FormActionRunState.Failed or FormActionRunState.NotApplicable or FormActionRunState.Uncertain
            || LeaseExpiresAtUtc>nowUtc || NextCheckAtUtc>nowUtc || lease<=TimeSpan.Zero || Fence==long.MaxValue) return FormActionErrors.StateConflict;
        var reconcile=State==FormActionRunState.Running;
        Fence++;State=FormActionRunState.Running;LeaseExpiresAtUtc=nowUtc.Add(lease);NextCheckAtUtc=null;UpdatedAtUtc=nowUtc;
        return new FormActionReservation(Fence,reconcile);
    }
    // A pending effect is asked again in 5 seconds, or later when its issuer says when (an issuance waiting for its provider).
    public Result RecordOutcome(long fence,DateTime nowUtc,FormActionRunState state,string? failureCode,bool canRetry,string? outcomeJson=null,DateTime? nextCheckAtUtc=null)
    {
        // NotApplicable after reserving: the response was corrected or voided before the action ran.
        if(!OwnsLease(fence,nowUtc) || state is not (FormActionRunState.Running or FormActionRunState.Completed or FormActionRunState.Failed or FormActionRunState.Uncertain or FormActionRunState.NotApplicable)) return FormActionErrors.StateConflict;
        State=state;FailureCode=failureCode;CanRetry=state==FormActionRunState.Failed&&canRetry;OutcomeJson=outcomeJson;UpdatedAtUtc=nowUtc;
        LeaseExpiresAtUtc=null;NextCheckAtUtc=state==FormActionRunState.Running?(nextCheckAtUtc>nowUtc.AddSeconds(5)?nextCheckAtUtc:nowUtc.AddSeconds(5)):null;
        CompletedAtUtc=state is FormActionRunState.Completed or FormActionRunState.Failed or FormActionRunState.NotApplicable?nowUtc:null;RetryRequested=false;RetryRecipientUserId=null;DeliveryReviewRequested=false;return Result.Success();
    }
    /// <summary>F2: the wait before the next automatic consult: 15 s, doubling, up to 15 minutes. A consult never sends again.</summary>
    public static TimeSpan AutoConsultDelay(int attempt)=>TimeSpan.FromSeconds(Math.Min(900,15*Math.Pow(2,Math.Max(0,attempt))));
    /// <summary>
    /// F2: an effect left in doubt by a local error is asked about again on its own, with a growing wait, up to <see cref="MaxAutoConsults"/>
    /// times; after that it stays uncertain, for a person. It is the same consult as the manual one (it never sends again).
    /// </summary>
    public Result RecordUnconfirmed(long fence,DateTime nowUtc,string failureCode,string? outcomeJson=null)
    {
        if(AutoConsultCount>=MaxAutoConsults)return RecordOutcome(fence,nowUtc,FormActionRunState.Uncertain,failureCode,false,outcomeJson);
        var recorded=RecordOutcome(fence,nowUtc,FormActionRunState.Running,null,false,outcomeJson,nowUtc.Add(AutoConsultDelay(AutoConsultCount)));
        if(recorded.IsSuccess)AutoConsultCount++;
        return recorded;
    }
    /// <summary>
    /// F2: an action that cannot go on by itself and stops the rest of its rule: failed, or in doubt waiting for a person (an issuance
    /// that waits for its author to confirm it is a question to them, not a stop). An expression, so a query can use it.
    /// </summary>
    public static Expression<Func<BusinessFormActionRun,bool>> UnnoticedStall { get; } = run => run.StallNoticedAtUtc==null
        && (run.State==FormActionRunState.Failed || run.State==FormActionRunState.Uncertain && run.FailureCode!=FormActionErrors.AwaitingConfirmation.Code);
    private static readonly Func<BusinessFormActionRun,bool> IsUnnoticedStall=UnnoticedStall.Compile();
    public bool NeedsStallNotice=>IsUnnoticedStall(this);
    /// <summary>F2: whoever loaded the response was told (or has no channel to be told): the run is not announced again, however many times it is retried.</summary>
    public void RecordStallNotice(DateTime nowUtc){StallNoticedAtUtc??=nowUtc;}
    public bool OwnsLease(long fence,DateTime nowUtc)=>Fence==fence&&State==FormActionRunState.Running&&LeaseExpiresAtUtc>nowUtc;
    private void Requested(string kind,Guid? byUserId,DateTime nowUtc){LastRequestKind=kind;LastRequestedByUserId=byUserId;LastRequestedAtUtc=nowUtc;}
    public Result RequestRetry(DateTime nowUtc,Guid? requestedBy=null)
    {
        if(State is FormActionRunState.Completed or FormActionRunState.NotApplicable || RetryRequested)return Result.Success();
        if(State!=FormActionRunState.Failed || !CanRetry)return FormActionErrors.StateConflict;
        Requested("Retry",requestedBy,nowUtc);
        State=FormActionRunState.Waiting;FailureCode=null;CompletedAtUtc=null;CanRetry=false;
        RetryRequested=true;NextCheckAtUtc=null;UpdatedAtUtc=nowUtc;return Result.Success();
    }
    // A role notice is done once every person got it or failed; only a person whose failure is confirmed is sent again,
    // and the people who got it are never repeated. Application checks the confirmed failure in the outcome.
    public Result RequestRecipientRetry(Guid recipientUserId,long expectedRevision,DateTime nowUtc,Guid? requestedBy=null)
    {
        if(RetryRequested && RetryRecipientUserId==recipientUserId && recipientUserId!=Guid.Empty)return Result.Success();
        if(Kind!="NotifyRole" || State!=FormActionRunState.Completed || Fence!=expectedRevision || recipientUserId==Guid.Empty)return FormActionErrors.StateConflict;
        Requested("RecipientRetry",requestedBy,nowUtc);
        State=FormActionRunState.Waiting;FailureCode=null;CompletedAtUtc=null;CanRetry=false;
        RetryRequested=true;RetryRecipientUserId=recipientUserId;NextCheckAtUtc=null;UpdatedAtUtc=nowUtc;return Result.Success();
    }
    public Result RequestDeliveryReview(Guid recipientUserId,long expectedRevision,bool wasSent,Guid reviewedByUserId,DateTime nowUtc)
    {
        if(DeliveryReviewRecipientUserId==recipientUserId && DeliveryReviewExpectedRevision==expectedRevision && DeliveryReviewWasSent==wasSent && DeliveryReviewedByUserId==reviewedByUserId)return Result.Success();
        if(Fence!=expectedRevision || State!=FormActionRunState.Uncertain || Kind is not ("NotifyAuthor" or "NotifyRole") || reviewedByUserId==Guid.Empty || recipientUserId==Guid.Empty)
            return FormActionErrors.StateConflict;
        DeliveryReviewRequested=true;DeliveryReviewRecipientUserId=recipientUserId;DeliveryReviewExpectedRevision=expectedRevision;DeliveryReviewWasSent=wasSent;DeliveryReviewedByUserId=reviewedByUserId;DeliveryReviewedAtUtc=nowUtc;
        State=FormActionRunState.Running;LeaseExpiresAtUtc=null;RetryRequested=false;RetryRecipientUserId=null;NextCheckAtUtc=null;UpdatedAtUtc=nowUtc;
        return Result.Success();
    }
    // An issuance in review waits for a person: proceed (another number, today's date) or not issue. It has no recipient, and
    // the decision travels in the review of the delivery: WasSent says whether to proceed.
    public Result RequestIssueReview(long expectedRevision,bool proceed,Guid reviewedByUserId,DateTime nowUtc)
    {
        if(DeliveryReviewRecipientUserId is null && DeliveryReviewExpectedRevision==expectedRevision && DeliveryReviewWasSent==proceed && DeliveryReviewedByUserId==reviewedByUserId)return Result.Success();
        if(Fence!=expectedRevision || State!=FormActionRunState.Uncertain || Kind!="IssueDocument" || reviewedByUserId==Guid.Empty)
            return FormActionErrors.StateConflict;
        DeliveryReviewRequested=true;DeliveryReviewRecipientUserId=null;DeliveryReviewExpectedRevision=expectedRevision;DeliveryReviewWasSent=proceed;DeliveryReviewedByUserId=reviewedByUserId;DeliveryReviewedAtUtc=nowUtc;
        State=FormActionRunState.Running;LeaseExpiresAtUtc=null;RetryRequested=false;RetryRecipientUserId=null;NextCheckAtUtc=null;UpdatedAtUtc=nowUtc;
        return Result.Success();
    }
    public bool RecordDecisionTap(Guid tapId)
    {
        if(tapId==Guid.Empty) return false;
        DecisionTapId=tapId;
        return true;
    }
    public Result RequestConsult(DateTime nowUtc,Guid? requestedBy=null)
    {
        if(State is FormActionRunState.Completed or FormActionRunState.NotApplicable) return Result.Success();
        if(State!=FormActionRunState.Uncertain && !(State==FormActionRunState.Running&&LeaseExpiresAtUtc<=nowUtc)) return FormActionErrors.StateConflict;
        if(requestedBy is not null)Requested("Consult",requestedBy,nowUtc);
        State=FormActionRunState.Running;LeaseExpiresAtUtc=null;
        RetryRequested=false;RetryRecipientUserId=null;NextCheckAtUtc=null;UpdatedAtUtc=nowUtc;return Result.Success();
    }
    /// <summary>
    /// A live issuance of its response, the one definition for voiding, correcting and every reader: issued, being issued, in
    /// review or waiting for whoever loaded it; neither its reversal (event «Voided») nor one that failed or did not apply. An
    /// expression, so a query can use it.
    /// </summary>
    public static Expression<Func<BusinessFormActionRun,bool>> LiveIssuance { get; } = run => run.Kind=="IssueDocument" && run.Event!="Voided"
        && run.State!=FormActionRunState.Failed && run.State!=FormActionRunState.NotApplicable;
    private static readonly Func<BusinessFormActionRun,bool> IsLive=LiveIssuance.Compile();
    public bool IsLiveIssuance=>IsLive(this);
    public override string ToString()=>$"BusinessFormActionRun {{ Id = {Id}, State = {State}, Content = [REDACTED] }}";
}
