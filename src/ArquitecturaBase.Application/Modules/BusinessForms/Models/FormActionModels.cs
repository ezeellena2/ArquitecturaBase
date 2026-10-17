using ArquitecturaBase.Domain.Modules.BusinessForms;
using ArquitecturaBase.Application.Models.StructuredData;
namespace ArquitecturaBase.Application.Modules.BusinessForms.Models;
public sealed record FormActionRecordSnapshot(FormResponseStoredRow Response,string OriginalCulture,FormActionDecisionSnapshot? Decision=null)
{ public override string ToString()=>"FormActionRecordSnapshot { Content = [REDACTED] }"; }
public sealed record FormActionDecisionSnapshot(Guid TaskId,Guid ActorUserId,string? ActorName,DateTime DecidedAtUtc,string Decision,string? Reason)
{ public override string ToString()=>"FormActionDecisionSnapshot { Content = [REDACTED] }"; }
public sealed record FormActionRecipientRow(Guid UserId,string Culture,string? DisplayName=null);
public sealed record FormActionRecipient(Guid UserId,string Culture,string Body,string? DisplayName=null,string? PreparationFailureCode=null)
{ public override string ToString()=>"FormActionRecipient { Content = [REDACTED] }"; }
public sealed record FormWorkflowResponse(Guid RecordId,string RecordState,long? RuleRevision,FormWorkflowTaskResponse? Task,
    IReadOnlyList<FormActionRunResponse> Actions,IReadOnlyList<FormWorkflowDocumentResponse> Documents);
public sealed record FormWorkflowTaskResponse(Guid Id,Guid RoleId,long Revision,string State,string? RoleName,DateTime? DecidedAtUtc,FormAuthorResponse? DecidedBy,string? Reason)
{ public override string ToString()=>"FormWorkflowTaskResponse { Content = [REDACTED] }"; }
public sealed record FormActionRunResponse(Guid Id,Guid RuleId,string Kind,string Event,int Order,string State,string? ReasonCode,string? Reason,
    bool CanConsult,bool CanRetry,DateTime CreatedAtUtc,DateTime UpdatedAtUtc,DateTime? CompletedAtUtc,
    long Revision,IReadOnlyList<FormActionRecipientResponse> Recipients);
public sealed record FormActionRecipientResponse(FormAuthorResponse User,string? ChannelId,string State,string? ReasonCode,string? Reason,
    bool CanReview,DateTime? DeliveredAtUtc,DateTime? ReadAtUtc,FormActionDeliveryReviewResponse? Review);
public sealed record FormActionRecipientOutcome(Guid UserId,string? DisplayName,string? ChannelId,StructuredRecordActionResponse Status);
public sealed record FormActionDeliveryReviewResponse(bool WasSent,DateTime ReviewedAtUtc,FormAuthorResponse ReviewedBy);
public sealed record FormWorkflowDocumentResponse(Guid Id,string Culture,string Name,long Size,DateTime CreatedAtUtc);
public sealed record FormActionSnapshot(Guid? ApprovalRoleId, FormPdfActionSnapshotResponse? Pdf,
    Guid? PdfRunId,IReadOnlyList<FormActionRecipient> Recipients,string? PreparationFailureCode=null)
{ public override string ToString()=>"FormActionSnapshot { Content = [REDACTED] }"; }
public sealed record FormActionWorkResponse(Guid RunId,Guid RecordId,string Kind,long Fence,bool ReconciliationOnly,bool RetryRequested,
    FormActionSnapshot Snapshot,IReadOnlyList<FormPdfArtifact> PdfArtifacts,StructuredRecordActionReview? DeliveryReview=null,Guid? DeliveryReviewRecipientUserId=null,IReadOnlyList<FormActionRecipientOutcome>? PriorRecipients=null)
{ public override string ToString()=>"FormActionWorkResponse { Content = [REDACTED] }"; }
public sealed record FormActionOutcomeResponse(FormActionRunState State,string? FailureCode=null,bool CanRetry=false,FormPdfActionOutcomeResponse? Pdf=null,IReadOnlyList<FormActionRecipientOutcome>? Recipients=null)
{ public override string ToString()=>"FormActionOutcomeResponse { Content = [REDACTED] }"; }
