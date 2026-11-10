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
    public static readonly Error FenceRejected = Error.Conflict("Ai.Assistant.FenceRejected", "The execution reservation is no longer valid.");
    public static readonly Error Canceled = Error.Conflict("Ai.Assistant.Canceled", "Cancellation was confirmed before publication.");
    public static readonly Error Expired = Error.Conflict("Ai.Assistant.Expired", "The assistant operation expired.");
    public static readonly Error LimitExceeded = Error.Conflict("Ai.Assistant.LimitExceeded", "The assistant operation exceeded its limits.");
    public static readonly Error InvalidOutput = Error.Conflict("Ai.Assistant.InvalidOutput", "The assistant output did not satisfy the required contract.");
    public static readonly Error ToolRejected = Error.Forbidden("Ai.Assistant.ToolRejected", "The requested query is not authorized or is invalid.");
    public static readonly Error ProviderUnavailable = Error.Failure("Ai.Assistant.ProviderUnavailable", "The provider is unavailable.");
    public static readonly Error ProviderUncertain = Error.Failure("Ai.Assistant.ProviderUncertain", "The provider request may have been sent without a confirmed result.");
    public static readonly Error ProviderRejected = Error.Conflict("Ai.Assistant.ProviderRejected", "The provider rejected the request.");
    public static readonly Error ProviderIncomplete = Error.Conflict("Ai.Assistant.ProviderIncomplete", "The provider returned an incomplete result.");
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
}
