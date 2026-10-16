using ArquitecturaBase.Domain.Results;
namespace ArquitecturaBase.Domain.Modules.BusinessForms;

public static class FormApprovalErrors
{
    public static readonly Error NotFound = Error.NotFound("Forms.Approval.NotFound", "The approval task was not found.");
    public static readonly Error RevisionConflict = Error.Conflict("Forms.Approval.RevisionConflict", "Another person already decided this task. Reload it.");
    public static readonly Error OwnResponse = Error.Forbidden("Forms.Approval.OwnResponse", "You cannot decide your own response.");
    public static readonly Error RoleChanged = Error.Forbidden("Forms.Approval.RoleChanged", "You no longer belong to the assigned role.");
    public static readonly Error ContentInvalid = Error.Validation("Forms.Approval.ContentInvalid", "The approval decision is invalid.");
    public static readonly Error ReasonRequired = Error.Validation("Forms.Approval.ReasonRequired", "A reason is required to reject the response.");
}
