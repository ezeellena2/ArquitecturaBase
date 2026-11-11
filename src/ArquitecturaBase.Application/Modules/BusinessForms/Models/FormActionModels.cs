using ArquitecturaBase.Domain.Modules.BusinessForms;
using ArquitecturaBase.Application.Models.StructuredData;
namespace ArquitecturaBase.Application.Modules.BusinessForms.Models;
public sealed record FormActionRecordRow(FormResponseStoredRow Response,string OriginalCulture,FormActionDecisionSnapshot? Decision=null)
{ public override string ToString()=>"FormActionRecordRow { Content = [REDACTED] }"; }
public sealed record FormActionDecisionSnapshot(Guid TaskId,Guid ActorUserId,string? ActorName,DateTime DecidedAtUtc,string Decision,string? Reason)
{ public override string ToString()=>"FormActionDecisionSnapshot { Content = [REDACTED] }"; }
public sealed record FormActionRecipientRow(Guid UserId,string Culture,string? DisplayName=null);
public sealed record FormActionRecipient(Guid UserId,string Culture,string Body,string? DisplayName=null,string? PreparationFailureCode=null)
{ public override string ToString()=>"FormActionRecipient { Content = [REDACTED] }"; }
public sealed record FormWorkflowResponse(Guid RecordId,string RecordState,long? RuleRevision,FormWorkflowTaskResponse? Task,
    IReadOnlyList<FormActionRunResponse> Actions,IReadOnlyList<FormWorkflowDocumentResponse> Documents)
{
    /// <summary>An issuance waiting for whoever loaded the response to confirm it («¿La emito?»), with its summary; absent otherwise.</summary>
    [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)]
    public FormIssueConfirmationResponse? Confirmation { get; init; }
}
public sealed record FormIssueConfirmationResponse(Guid RunId,IReadOnlyDictionary<string,string>? Document,string? Receiver,string? Total)
{
    /// <summary>Why the issuer decided that document, by language, and the amounts it itemizes before the total.</summary>
    [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)]
    public IReadOnlyDictionary<string,string>? Info { get; init; }
    [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)]
    public string? Date { get; init; }
    [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)]
    public IReadOnlyList<ArquitecturaBase.Application.Models.StructuredData.StructuredIssueAmount>? Amounts { get; init; }
}
public sealed record FormWorkflowTaskResponse(Guid Id,Guid RoleId,long Revision,string State,string? RoleName,DateTime? DecidedAtUtc,FormAuthorResponse? DecidedBy,string? Reason,DateTime RequestedAtUtc)
{ public override string ToString()=>"FormWorkflowTaskResponse { Content = [REDACTED] }"; }
public sealed record FormActionRunResponse(Guid Id,Guid RuleId,string Kind,string Event,int Order,string State,string? ReasonCode,string? Reason,
    bool CanConsult,bool CanRetry,DateTime CreatedAtUtc,DateTime UpdatedAtUtc,DateTime? CompletedAtUtc,
    long Revision,IReadOnlyList<FormActionRecipientResponse> Recipients)
{
    /// <summary>An issuance in review that this person may decide; written only when true.</summary>
    [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingDefault)]
    public bool CanReview { get; init; }
}
public sealed record FormActionRecipientResponse(FormAuthorResponse User,string? ChannelId,string State,string? ReasonCode,string? Reason,
    bool CanReview,bool CanRetry,DateTime? DeliveredAtUtc,DateTime? ReadAtUtc,FormActionDeliveryReviewResponse? Review);
public sealed record FormActionRecipientOutcome(Guid UserId,string? DisplayName,string? ChannelId,StructuredRecordActionResponse Status);
public sealed record FormActionDeliveryReviewResponse(bool WasSent,DateTime ReviewedAtUtc,FormAuthorResponse ReviewedBy);
public sealed record FormWorkflowDocumentResponse(Guid Id,string Culture,string Name,long Size,DateTime CreatedAtUtc)
{ public Guid RunId { get; init; } }
public sealed record FormActionSnapshot(Guid? ApprovalRoleId, FormPdfActionSnapshotResponse? Pdf,
    Guid? PdfRunId,IReadOnlyList<FormActionRecipient> Recipients,string? PreparationFailureCode=null,
    [property: System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)] string? IssuerId=null,
    [property: System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)] Guid? ReversesRunId=null,
    [property: System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)] string? VoidReason=null,
    [property: System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)] Guid? VoidedByUserId=null,
    [property: System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)] string? CopyBody=null)
{ public override string ToString()=>"FormActionSnapshot { Content = [REDACTED] }"; }
public sealed record FormActionWorkResponse(Guid RunId,Guid RecordId,string Kind,long Fence,bool ReconciliationOnly,bool RetryRequested,
    FormActionSnapshot Snapshot,IReadOnlyList<FormPdfArtifact> PdfArtifacts,StructuredRecordActionReview? DeliveryReview=null,Guid? DeliveryReviewRecipientUserId=null,IReadOnlyList<FormActionRecipientOutcome>? PriorRecipients=null,
    Guid? RetryRecipientUserId=null)
{
    /// <summary>An issuance is queued only while its response stands: not voided nor replaced by a correction.</summary>
    public bool RecordStands { get; init; } = true;
    /// <summary>A response loaded in the app was confirmed before sending («¿La emito?»); one from a channel waits for it.</summary>
    public bool RecordEnteredInApp { get; init; } = true;
    /// <summary>The issuance as the run last saw it, for the revision a review expects.</summary>
    public StructuredIssueResponse? PriorIssue { get; init; }
    public override string ToString()=>"FormActionWorkResponse { Content = [REDACTED] }";
}
public sealed record FormActionOutcomeResponse(FormActionRunState State,string? FailureCode=null,bool CanRetry=false,FormPdfActionOutcomeResponse? Pdf=null,IReadOnlyList<FormActionRecipientOutcome>? Recipients=null,
    [property: System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)] StructuredIssueResponse? Issue=null,
    [property: System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)] StructuredIssuePreview? IssuePreview=null)
{
    /// <summary>The copy by email of a «SendCopy», queued with the confirmation of its run; never stored with the outcome.</summary>
    [System.Text.Json.Serialization.JsonIgnore]
    public ArquitecturaBase.Application.Models.Emails.EmailMessage? Email { get; init; }
    public override string ToString()=>"FormActionOutcomeResponse { Content = [REDACTED] }";
}
