using ArquitecturaBase.Domain.Results;
namespace ArquitecturaBase.Domain.Modules.BusinessForms;

/// <summary>«Documentos recibidos» (DOC5-A): the inbox of the documents people send, with its copy, reading, proposal and history.</summary>
public static class FormReceivedDocumentErrors
{
    public static readonly Error NotFound = Error.NotFound("Forms.ReceivedDocument.NotFound", "The received document was not found.");
    public static readonly Error ContentInvalid = Error.Validation("Forms.ReceivedDocument.ContentInvalid", "The received document request is invalid.");
    public static readonly Error RevisionConflict = Error.Conflict("Forms.ReceivedDocument.RevisionConflict", "The received document changed.");
    public static readonly Error StateConflict = Error.Conflict("Forms.ReceivedDocument.StateConflict", "The received document cannot do that in its state.");
    public static readonly Error ReadingUnavailable = Error.Conflict("Forms.ReceivedDocument.ReadingUnavailable", "The received document has no reading.");
    public static readonly Error FormUnavailable = Error.Conflict("Forms.ReceivedDocument.FormUnavailable", "The form is not published and active.");
    public static readonly Error SenderInactive = Error.Conflict("Forms.ReceivedDocument.SenderInactive", "Whoever sent the document is no longer active.");
    public static readonly Error ExtractionNotFound = Error.NotFound("Forms.ReceivedDocument.ExtractionNotFound", "The extraction was not found.");
    public static readonly Error FileUnavailable = Error.Conflict("Forms.ReceivedDocument.FileUnavailable", "The copy of the received document is unavailable.");
    public static readonly Error FileNotStored = Error.NotFound("Forms.ReceivedDocument.FileNotStored", "The received document was kept without its file.");
    public static readonly Error AwaitingSender = Error.Conflict("Forms.ReceivedDocument.AwaitingSender", "Whoever sent the document can still complete it.");
    public static readonly Error RequestConflict = Error.Conflict("Forms.ReceivedDocument.RequestConflict", "The request was already used with other data.");
    public static readonly Error ExtractionUnavailable = Error.Failure("Forms.ReceivedDocument.ExtractionUnavailable", "The extraction could not be read.");
    // DOC6, «Armar formulario con la IA»: a proposal built from one unclassified document or from a group of similar ones.
    public static readonly Error ProposalNotFound = Error.NotFound("Forms.ReceivedDocument.ProposalNotFound", "The form proposal was not found.");
    public static readonly Error ProposalUnavailable = Error.Failure("Forms.ReceivedDocument.ProposalUnavailable", "The form proposal could not be asked for or read.");
    public static readonly Error GroupUnavailable = Error.Conflict("Forms.ReceivedDocument.GroupUnavailable", "No unclassified document of the group can be read.");
    // DOC6, the re-reading after publishing: the assistant paused, not configured or not qualified for now; the server tries again.
    public static readonly Error AssistantUnavailable = Error.Conflict("Forms.ReceivedDocument.AssistantUnavailable",
        "The assistant is not available for now; the server tries again in a moment.", new Dictionary<string, object?> { ["retryable"] = true });
}
