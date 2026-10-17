using ArquitecturaBase.Domain.Results;
namespace ArquitecturaBase.Domain.Modules.BusinessForms;
public static class FormSuggestionErrors
{
    public static readonly Error ContentInvalid = Error.Validation("Forms.Suggestion.ContentInvalid", "The form suggestion is invalid.");
    public static readonly Error RevisionConflict = Error.Conflict("Forms.Suggestion.RevisionConflict", "The form suggestions changed.");
    public static readonly Error SourceNotPublished = Error.Conflict("Forms.Suggestion.SourceNotPublished", "Publish the source form before configuring suggestions.");
    public static readonly Error RevisionNotFound = Error.NotFound("Forms.Suggestion.RevisionNotFound", "The form suggestion revision was not found.");
}
