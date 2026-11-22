using ArquitecturaBase.Domain.Results;
using ArquitecturaBase.Domain.Common;
namespace ArquitecturaBase.Domain.Modules.BusinessForms;

/// <summary>Closed: the response was corrected or voided before anyone decided.</summary>
public enum FormTaskState { Open, Approved, Rejected, Closed }
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
    /// <summary>«Puede decidir quien cargó» of the approval that opened it: whoever loaded, corrected or passed the response may decide it too.</summary>
    public bool AuthorMayDecide { get; private set; }
    /// <summary>«Exigir motivo al rechazar» unchecked in the approval that opened it: a rejection may come without a reason.</summary>
    public bool ReasonOptional { get; private set; }
    public DateTime CreatedAtUtc { get; private set; }
    public Guid? CreatedBy { get; private set; }
    public DateTime? ModifiedAtUtc { get; private set; }
    public Guid? ModifiedBy { get; private set; }
    /// <summary>The choices of the approval travel with the task, from the rule revision the response arrived with (RC4-A).</summary>
    public static BusinessFormTask Open(Guid definitionId, Guid recordId, Guid ruleSetId, Guid runId, Guid roleId, Guid authorUserId, DateTime nowUtc,
        bool authorMayDecide = false, bool reasonOptional = false) =>
        new() { DefinitionId = definitionId, RecordId = recordId, RuleSetId = ruleSetId, RunId = runId, RoleId = roleId, AuthorUserId = authorUserId, RequestedAtUtc = nowUtc,
            AuthorMayDecide = authorMayDecide, ReasonOptional = reasonOptional };
    public Result Decide(long expectedRevision, Guid actorUserId, bool approved, string? reason, DateTime nowUtc)
    {
        if (State != FormTaskState.Open || Revision != expectedRevision || Revision >= 9007199254740991) return FormApprovalErrors.RevisionConflict;
        if (!AuthorMayDecide && actorUserId == AuthorUserId) return FormApprovalErrors.OwnResponse;
        var normalized = reason?.Trim();
        if (!approved && !ReasonOptional && string.IsNullOrWhiteSpace(normalized)) return FormApprovalErrors.ReasonRequired;
        if (nowUtc.Kind != DateTimeKind.Utc || actorUserId == Guid.Empty || normalized?.Length > 500
            || normalized is not null && !StorableText.IsStorable(normalized) || approved && normalized is not null) return FormApprovalErrors.ContentInvalid;
        State = approved ? FormTaskState.Approved : FormTaskState.Rejected;
        Revision++;
        DecidedByUserId = actorUserId; DecidedAtUtc = nowUtc; Reason = approved || string.IsNullOrEmpty(normalized) ? null : normalized;
        return Result.Success();
    }
    /// <summary>The response it waited for was corrected or voided: nobody decides it anymore.</summary>
    public Result Close()
    {
        if (State != FormTaskState.Open || Revision >= 9007199254740991) return FormApprovalErrors.RevisionConflict;
        State = FormTaskState.Closed;
        Revision++;
        return Result.Success();
    }
    public override string ToString() => $"BusinessFormTask {{ Id = {Id}, State = {State}, Content = [REDACTED] }}";
}
