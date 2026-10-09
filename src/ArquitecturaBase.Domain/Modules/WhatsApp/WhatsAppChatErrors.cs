using ArquitecturaBase.Domain.Results;

namespace ArquitecturaBase.Domain.Modules.WhatsApp;

public static class WhatsAppChatErrors
{
    public const string NotFoundCode = "WhatsApp.Chat.NotFound";
    public const string ManualRequiredCode = "WhatsApp.Chat.ManualRequired";
    public const string RevisionConflictCode = "WhatsApp.Chat.RevisionConflict";
    public const string SendPendingCode = "WhatsApp.Chat.SendPending";
    public const string WindowClosedCode = "WhatsApp.Chat.WindowClosed";
    public const string DestinationMissingCode = "WhatsApp.Chat.DestinationMissing";
    public const string RequestConflictCode = "WhatsApp.Chat.RequestConflict";
    public const string ReadPositionInvalidCode = "WhatsApp.Chat.ReadPositionInvalid";

    public static readonly Error NotFound = Error.NotFound(NotFoundCode, "The conversation was not found.");
    public static readonly Error ManualRequired = Error.Conflict(ManualRequiredCode, "Switch to manual mode before replying.");
    public static readonly Error RevisionConflict = Error.Conflict(RevisionConflictCode, "The conversation mode changed.");
    public static readonly Error SendPending = Error.Conflict(SendPendingCode, "A message is pending or its outcome needs review.");
    public static readonly Error WindowClosed = Error.Conflict(WindowClosedCode, "The customer service window is closed.");
    public static readonly Error DestinationMissing = Error.Conflict(DestinationMissingCode, "This contact has no supported reply destination.");
    public static readonly Error RequestConflict = Error.Conflict(RequestConflictCode, "This request id belongs to different message content.");
    public static readonly Error ReadPositionInvalid = Error.Validation(ReadPositionInvalidCode, "The read position is outside the stored history.");
}
