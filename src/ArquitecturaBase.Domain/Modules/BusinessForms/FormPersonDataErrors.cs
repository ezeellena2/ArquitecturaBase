using ArquitecturaBase.Domain.Results;
namespace ArquitecturaBase.Domain.Modules.BusinessForms;
public static class FormPersonDataErrors
{
    public static readonly Error AttributeNotFound = Error.NotFound("Forms.PersonAttribute.NotFound", "The person attribute was not found.");
    public static readonly Error AttributeRevisionConflict = Error.Conflict("Forms.PersonAttribute.RevisionConflict", "The person attribute changed. Reload it.");
    public static readonly Error AttributeKeyDuplicate = Error.Validation("Forms.PersonAttribute.KeyDuplicate", "A person attribute with this key already exists.");
    public static readonly Error AttributeInUse = Error.Conflict("Forms.PersonAttribute.InUse", "A filter uses this person attribute.");
    public static readonly Error AttributeTooMany = Error.Conflict("Forms.PersonAttribute.TooMany", "Up to 20 person attributes can be defined.");
    public static readonly Error AttributeInvalid = Error.Validation("Forms.PersonAttribute.Invalid", "The person attribute definition is invalid.");
    public static readonly Error SourceUnavailable = Error.Conflict("Forms.PersonAttribute.SourceUnavailable", "The source catalog is unavailable.");
    public static readonly Error RevisionConflict = Error.Conflict("Forms.PersonData.RevisionConflict", "The person data changed. Reload it.");
    public static readonly Error ValueInvalid = Error.Validation("Forms.PersonData.ValueInvalid", "The value does not match the person attribute.");
    public static readonly Error UserNotFound = Error.NotFound("Forms.PersonData.UserNotFound", "The person was not found.");
}
