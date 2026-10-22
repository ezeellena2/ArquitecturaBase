using ArquitecturaBase.Application.Common.Pagination;
namespace ArquitecturaBase.Application.Modules.BusinessForms.Models;

public sealed record ListFormApprovalsRequest : PagedRequest
{
    public static readonly IReadOnlyCollection<string> SortableFields = [];
    public Guid? DefinitionId { get; init; }
    public bool OnlyMine { get; init; } = true;
}
public sealed record FormApprovalFilterRequest(Guid? DefinitionId = null, bool OnlyMine = true);
public sealed record FormApprovalCountResponse(int Count);
public sealed record FormApprovalTaskResponse(Guid Id, Guid DefinitionId, Guid RecordId, string Number, string FormName,
    FormAuthorResponse Author, FormRuleRoleResponse Role, DateTime RequestedAtUtc, long Revision, string State, bool CanDecide, string? CannotDecideReason,
    DateTime? DecidedAtUtc, FormAuthorResponse? DecidedBy, string? Reason)
{ public override string ToString() => "FormApprovalTaskResponse { Content = [REDACTED] }"; }
public sealed record DecideFormApprovalRequest(long? ExpectedRevision, string? Decision, string? Reason = null)
{ public override string ToString() => "DecideFormApprovalRequest { Content = [REDACTED] }"; }
public sealed record FormApprovalDecisionResponse(Guid TaskId, Guid RecordId, long Revision, string State, DateTime DecidedAtUtc, FormAuthorResponse DecidedBy, string? Reason)
{ public override string ToString() => "FormApprovalDecisionResponse { Content = [REDACTED] }"; }
public sealed record FormApprovalActorRow(Guid UserId, string? DisplayName, bool IsActive, bool IsInRole);

public sealed record FormApprovalContinuationRequest(Guid DefinitionId, string? Direction, int PageSize = 20);
public sealed record FormApprovalContinuationItemResponse(FormApprovalTaskResponse Task, int Position)
{ public override string ToString() => "FormApprovalContinuationItemResponse { Content = [REDACTED] }"; }
public sealed record FormApprovalContinuationResponse(Guid AnchorTaskId, Guid DefinitionId, string Direction,
    IReadOnlyList<FormApprovalContinuationItemResponse> Items, int TotalCount, bool HasMore)
{ public override string ToString() => "FormApprovalContinuationResponse { Content = [REDACTED] }"; }
