using ArquitecturaBase.Domain.Results;

namespace ArquitecturaBase.Domain.Modules.ArtificialIntelligence;

public static class AiAssistantErrors
{
    public static readonly Error Invalid = Error.Validation("Ai.Assistant.Invalid", "The assistant request is invalid.");
    public static readonly Error SessionRequired = Error.Unauthorized("Ai.Assistant.SessionRequired", "An authenticated account is required.");
    public static readonly Error Forbidden = Error.Forbidden("Ai.Assistant.Forbidden", "The assistant is not authorized for this account.");
    public static readonly Error NotFound = Error.NotFound("Ai.Assistant.NotFound", "The assistant resource was not found.");
    public static readonly Error RequestReused = Error.Conflict("Ai.Assistant.RequestReused", "The request identifier was used with another question.");
    public static readonly Error RevisionConflict = Error.Conflict("Ai.Assistant.RevisionConflict", "The conversation changed. Review its current revision.");
    public static readonly Error ConnectionChanged = Error.Conflict("Ai.Assistant.ConnectionChanged", "The assistant connection changed.");
    public static readonly Error ActiveTurn = Error.Conflict("Ai.Assistant.ActiveTurn", "An assistant operation is already active.", new Dictionary<string, object?> { ["retryable"] = true });
    public static readonly Error Unavailable = Error.Conflict("Ai.Assistant.Unavailable", "The assistant is not available.");
    public static readonly Error ContentBlocked = Error.Forbidden("Ai.Assistant.ContentBlocked", "The conversation requires permissions that are no longer available.");
    public static readonly Error ContentUnavailable = Error.Failure("Ai.Assistant.ContentUnavailable", "The protected assistant content is unavailable.");
    // I4: a seal with a value this version does not know (a purpose, a kind of file, a mode, instructions, a capability or a terminal) was
    // written by a newer one. It is neither damaged nor absent: it may pass once the version that knows it answers.
    public static readonly Error UnknownVersion = Error.Conflict("Ai.Assistant.UnknownVersion", "The operation was sealed with a value this version does not know.",
        new Dictionary<string, object?> { ["retryable"] = true });
    public static readonly Error FenceRejected = Error.Conflict("Ai.Assistant.FenceRejected", "The execution reservation is no longer valid.");
    public static readonly Error Canceled = Error.Conflict("Ai.Assistant.Canceled", "Cancellation was confirmed before publication.");
    public static readonly Error Expired = Error.Conflict("Ai.Assistant.Expired", "The assistant operation expired.");
    public static readonly Error LimitExceeded = Error.Conflict("Ai.Assistant.LimitExceeded", "The assistant operation exceeded its limits.");
    // B1: the history reserves the latest proposal first; one that alone exceeds the history budget fails the turn visibly.
    public static readonly Error ProposalTooLarge = Error.Conflict("Ai.Assistant.ProposalTooLarge", "The latest proposal does not fit the conversation history.");
    public static readonly Error InvalidOutput = Error.Conflict("Ai.Assistant.InvalidOutput", "The assistant output did not satisfy the required contract.");
    public static readonly Error ToolRejected = Error.Forbidden("Ai.Assistant.ToolRejected", "The requested query is not authorized or is invalid.");
    public static readonly Error ProviderUnavailable = Error.Failure("Ai.Assistant.ProviderUnavailable", "The provider is unavailable.");
    // I5: an exception of the engine itself, outside the provider call: never blamed on the provider.
    public static readonly Error ExecutionFailed = Error.Failure("Ai.Assistant.ExecutionFailed", "The operation failed outside the provider call.");
    public static readonly Error ProviderUncertain = Error.Failure("Ai.Assistant.ProviderUncertain", "The provider request may have been sent without a confirmed result.");
    // I12: the copy running the operation stopped after reserving a step; one without a step goes back to pending instead.
    public static readonly Error Interrupted = Error.Failure("Ai.Assistant.Interrupted", "The copy running the operation stopped after it reached the provider.");
    public static readonly Error ProviderRejected = Error.Conflict("Ai.Assistant.ProviderRejected", "The provider rejected the request.");
    public static readonly Error ProviderIncomplete = Error.Conflict("Ai.Assistant.ProviderIncomplete", "The provider returned an incomplete result.");
    // I6: what the provider answered, by its HTTP status and never by its body, each with a text for whoever administers it.
    public static readonly Error ProviderCredentialRejected = Error.Conflict("Ai.Assistant.ProviderCredentialRejected", "The provider rejected the connection's key.");
    public static readonly Error ProviderLimited = Error.Conflict("Ai.Assistant.ProviderLimited", "The provider account reached its quota or rate limit.");
    public static readonly Error ProviderModelRejected = Error.Conflict("Ai.Assistant.ProviderModelRejected", "The provider rejected the model or the request.");
    /// <summary>I6: the key of the HTTP status a provider answered, which travels with its error only for the turn's log.</summary>
    public const string ProviderStatusKey = "providerStatus";
    public static Error WithProviderStatus(Error error, int status) =>
        error with { Metadata = new Dictionary<string, object?> { [ProviderStatusKey] = status } };
    public static int? ProviderStatus(Error error) =>
        error.Metadata is { } metadata && metadata.TryGetValue(ProviderStatusKey, out var value) && value is int status ? status : null;
    public static Error QuotaExceeded(int retryAfterSeconds) => Error.TooManyRequests("Ai.Assistant.QuotaExceeded", "The acceptance quota is exhausted.", new Dictionary<string, object?> { ["retryAfter"] = retryAfterSeconds });
    public static Error CapacityLimited(int retryAfterSeconds) => Error.TooManyRequests("Ai.Assistant.CapacityLimited", "The assistant capacity is exhausted.", new Dictionary<string, object?> { ["retryAfter"] = retryAfterSeconds, ["retryable"] = true });
    // Stage 2: who may attach a spreadsheet in Home, and why one was not read; each reason has its own code under the file.
    public static readonly Error AttachmentForbidden = Error.Forbidden("Ai.Assistant.AttachmentForbidden", "Attaching spreadsheets is not available for this account.");
    public static readonly Error AttachmentUnavailable = Error.Validation("Ai.Assistant.AttachmentUnavailable", "No spreadsheet reader is installed.");
    public static readonly Error AttachmentLegacy = Error.Validation("Ai.Assistant.AttachmentLegacy", "The spreadsheet is an Excel file older than 2007.");
    public static readonly Error AttachmentProtected = Error.Validation("Ai.Assistant.AttachmentProtected", "The spreadsheet is protected with a password.");
    public static readonly Error AttachmentMacroEnabled = Error.Validation("Ai.Assistant.AttachmentMacroEnabled", "The spreadsheet is macro-enabled or binary.");
    public static readonly Error AttachmentTooLarge = Error.Validation("Ai.Assistant.AttachmentTooLarge", "The spreadsheet exceeds its size, row or column limits.");
    public static readonly Error AttachmentUnreadable = Error.Validation("Ai.Assistant.AttachmentUnreadable", "The spreadsheet cannot be read.");
    public static readonly Error AttachmentEmpty = Error.Validation("Ai.Assistant.AttachmentEmpty", "The spreadsheet has no rows with content.");
    public static readonly Error AttachmentNotStored = Error.Failure("Ai.Assistant.AttachmentNotStored", "The private copy of the spreadsheet could not be read back.");
    // Stage 3: a document in Home (a photo, a PDF or a Word): who may attach one, whether the installation reads them, and why one was
    // not read, with a text that names what the file was.
    public static readonly Error AttachmentDocumentForbidden = Error.Forbidden("Ai.Assistant.AttachmentDocumentForbidden", "Attaching documents is not available for this account.");
    public static readonly Error AttachmentDocumentUnavailable = Error.Validation("Ai.Assistant.AttachmentDocumentUnavailable", "This installation does not read photos or PDF files.");
    public static readonly Error AttachmentDocumentNotStored = Error.Failure("Ai.Assistant.AttachmentDocumentNotStored", "The private copy of the document could not be read back.");
    public static readonly Error AttachmentTooManyPages = Error.Validation("Ai.Assistant.AttachmentTooManyPages", "The PDF has more than ten pages.");
    public static readonly Error AttachmentTooLargeImage = Error.Validation("Ai.Assistant.AttachmentTooLargeImage", "The photo exceeds its pixel limits.");
    public static readonly Error AttachmentProtectedPdf = Error.Validation("Ai.Assistant.AttachmentProtectedPdf", "The PDF is protected with a password.");
    public static readonly Error AttachmentProtectedWord = Error.Validation("Ai.Assistant.AttachmentProtectedWord", "The Word document is protected with a password.");
    public static readonly Error AttachmentLegacyWord = Error.Validation("Ai.Assistant.AttachmentLegacyWord", "The Word document is older than 2007.");
    public static readonly Error AttachmentMacroEnabledWord = Error.Validation("Ai.Assistant.AttachmentMacroEnabledWord", "The Word document is macro-enabled.");
    public static readonly Error AttachmentEmptyWord = Error.Validation("Ai.Assistant.AttachmentEmptyWord", "The Word document has no text.");
    public static readonly Error AttachmentUnreadablePdf = Error.Validation("Ai.Assistant.AttachmentUnreadablePdf", "The PDF cannot be read.");
    public static readonly Error AttachmentUnreadableImage = Error.Validation("Ai.Assistant.AttachmentUnreadableImage", "The photo cannot be read.");
    public static readonly Error AttachmentUnreadableWord = Error.Validation("Ai.Assistant.AttachmentUnreadableWord", "The Word document cannot be read.");
}
