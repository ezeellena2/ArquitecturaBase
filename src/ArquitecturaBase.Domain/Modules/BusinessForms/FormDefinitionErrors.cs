using ArquitecturaBase.Domain.Results;
namespace ArquitecturaBase.Domain.Modules.BusinessForms;
public static class FormDefinitionErrors
{
    public static readonly Error Unauthorized = Error.Unauthorized("Forms.Definition.Unauthorized", "A session is required.");
    public static readonly Error Forbidden = Error.Forbidden("Forms.Definition.Forbidden", "The required form permission is missing.");
    public static readonly Error DeactivationUnavailable = Error.Conflict("Forms.Definition.DeactivationUnavailable", "Only active published forms can be deactivated.");
    public static readonly Error NotFound = Error.NotFound("Forms.Definition.NotFound", "The form definition was not found.");
    public static readonly Error PrefixTaken = Error.Validation("Forms.Definition.PrefixTaken", "The prefix is already in use.");
    public static readonly Error PrefixLocked = Error.Conflict("Forms.Definition.PrefixLocked", "The prefix cannot be changed.");
    public static readonly Error RequestConflict = Error.Conflict("Forms.Draft.RequestConflict", "The request ID belongs to a different change.");
    public static readonly Error ChangeNotFound = Error.NotFound("Forms.Draft.ChangeNotFound", "The change was not found.");
    public static readonly Error ChangeExpired = new("Forms.Draft.ChangeExpired", "The protected result has expired.", ErrorType.Gone);
    public static readonly Error TranslationsWouldBeLost = Error.Validation("Forms.Draft.TranslationsWouldBeLost", "Preserve existing localized text or explicitly clear optional text.");
    public static readonly Error ContentInvalid = Error.Validation("Forms.Draft.ContentInvalid", "The draft content is invalid.");
    public static readonly Error ResultTooLarge = Error.Validation("Forms.Draft.ResultTooLarge", "The protected result exceeds the authoring budget.");
}
