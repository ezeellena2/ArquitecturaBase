using ArquitecturaBase.Domain.Results;
using ArquitecturaBase.Domain.Common;
namespace ArquitecturaBase.Domain.Modules.BusinessForms;

public enum FormTaskState { Open, Approved, Rejected }
public sealed class BusinessFormTask : Entity, IAuditable
{
    private BusinessFormTask() { }
    public Guid DefinitionId { get; private set; }
    public Guid RecordId { get; private set; }
    public Guid RuleSetId { get; private set; }
    public Guid RunId { get; private set; }
    public Guid RoleId { get; private set; }
    public Guid AuthorUserId { get; private set; }
    public long Revision { get; private set; } = 1;
    public FormTaskState State { get; private set; } = FormTaskState.Open;
    public DateTime RequestedAtUtc { get; private set; }
    public Guid? DecidedByUserId { get; private set; }
    public DateTime? DecidedAtUtc { get; private set; }
    public string? Reason { get; private set; }
    public DateTime CreatedAtUtc { get; private set; }
    public Guid? CreatedBy { get; private set; }
    public DateTime? ModifiedAtUtc { get; private set; }
    public Guid? ModifiedBy { get; private set; }
    public static BusinessFormTask Open(Guid definitionId, Guid recordId, Guid ruleSetId, Guid runId, Guid roleId, Guid authorUserId, DateTime nowUtc) =>
        new() { DefinitionId = definitionId, RecordId = recordId, RuleSetId = ruleSetId, RunId = runId, RoleId = roleId, AuthorUserId = authorUserId, RequestedAtUtc = nowUtc };
    public Result Decide(long expectedRevision, Guid actorUserId, bool approved, string? reason, DateTime nowUtc)
    {
        if (State != FormTaskState.Open || Revision != expectedRevision || Revision >= 9007199254740991) return FormApprovalErrors.RevisionConflict;
        if (actorUserId == AuthorUserId) return FormApprovalErrors.OwnResponse;
        var normalized = reason?.Trim();
        if (!approved && string.IsNullOrWhiteSpace(normalized)) return FormApprovalErrors.ReasonRequired;
        if (nowUtc.Kind != DateTimeKind.Utc || actorUserId == Guid.Empty || normalized?.Length > 500
            || normalized is not null && !StorableText.IsStorable(normalized) || approved && normalized is not null) return FormApprovalErrors.ContentInvalid;
        State = approved ? FormTaskState.Approved : FormTaskState.Rejected;
        Revision++;
        DecidedByUserId = actorUserId; DecidedAtUtc = nowUtc; Reason = approved ? null : normalized;
        return Result.Success();
    }
    public override string ToString() => $"BusinessFormTask {{ Id = {Id}, State = {State}, Content = [REDACTED] }}";
}
