using ArquitecturaBase.Domain.Results;
namespace ArquitecturaBase.Domain.Modules.BusinessForms;
public static class FormRuleErrors
{
    public static readonly Error ContentInvalid = Error.Validation("Forms.Rule.ContentInvalid", "The rule configuration is invalid.");
    public static readonly Error RevisionConflict = Error.Conflict("Forms.Rule.RevisionConflict", "The rules changed. Reload them and try again.");
    public static readonly Error RevisionNotFound = Error.NotFound("Forms.Rule.RevisionNotFound", "The rule revision was not found.");
    public static readonly Error RoleUnavailable = Error.Validation("Forms.Rule.RoleUnavailable", "The selected role is no longer available.");
    public static readonly Error PlaceholderInvalid = Error.Validation("Forms.Rule.PlaceholderInvalid", "The message contains an unknown or ambiguous placeholder.");
}
