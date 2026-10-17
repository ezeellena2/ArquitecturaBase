using ArquitecturaBase.Domain.Results;
namespace ArquitecturaBase.Domain.Modules.BusinessForms;
public static class FormMissingRequestErrors
{
    public static readonly Error ContentInvalid = Error.Validation("Forms.MissingRequest.ContentInvalid", "The missing form request is invalid.");
    public static readonly Error RevisionConflict = Error.Conflict("Forms.MissingRequest.RevisionConflict", "The missing form topic changed.");
    public static readonly Error NotFound = Error.NotFound("Forms.MissingRequest.NotFound", "The missing form topic was not found.");
}
