using ArquitecturaBase.Domain.Results;

namespace ArquitecturaBase.Domain.Modules.BusinessForms;

public static class FormDraftErrors
{
    public static readonly Error RevisionConflict = Error.Conflict(
        "Forms.Draft.RevisionConflict", "The draft changed. Reload it before saving.");

    public static readonly Error QuestionTypeUnavailable = Error.Validation(
        "Forms.Draft.QuestionTypeUnavailable", "The question type is not available in this stage.");

    public static readonly Error IdentityInvalid = Error.Validation(
        "Forms.Draft.IdentityInvalid", "The identity does not belong to this draft or question.");

    public static readonly Error IdentityRetired = Error.Conflict(
        "Forms.Draft.IdentityRetired", "A retired identity cannot be reused.");

    public static readonly Error QuestionConfigurationInvalid = Error.Validation(
        "Forms.Draft.QuestionConfigurationInvalid", "The question configuration is not valid for its type.");

    public static readonly Error ChoiceLabelRequired = Error.Validation(
        "Forms.Draft.ChoiceLabelRequired", "A choice requires a nonblank label.");
    public static readonly Error BoundQuestionChanged = Error.Validation("Forms.Draft.BoundQuestionChanged", "A question the form's standard needs keeps everything but its text, help, order and what it receives.");
    // W3 (UC1-A): the question that receives a location or a contact the person shares on their own by a channel.
    public static readonly Error ReceivesQuestionInvalid = Error.Validation("Forms.Draft.ReceivesQuestionInvalid", "Only a text question outside the block, without a condition or a fixed validation, receives a shared location or contact.");
    public static readonly Error ReceivesTaken = Error.Validation("Forms.Draft.ReceivesTaken", "Another question already receives this shared content.");
    public static readonly Error ReceivesLocationAsked = Error.Validation("Forms.Draft.ReceivesLocationAsked", "The form asks for the location with a location question.");
}
