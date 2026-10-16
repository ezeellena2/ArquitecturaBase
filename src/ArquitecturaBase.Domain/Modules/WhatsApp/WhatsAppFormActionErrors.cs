using ArquitecturaBase.Domain.Results;
namespace ArquitecturaBase.Domain.Modules.WhatsApp;

public static class WhatsAppFormActionErrors
{
    public static readonly Error ChannelUnavailable = Error.Conflict("WhatsApp.FormAction.ChannelUnavailable", "The action delivery channel is unavailable.");
    public static readonly Error RecipientUnavailable = Error.Conflict("WhatsApp.FormAction.RecipientUnavailable", "The action recipient is unavailable.");
    public static readonly Error RecipientChanged = Error.Conflict("WhatsApp.FormAction.RecipientChanged", "The accepted recipient context changed.");
    public static readonly Error WindowClosed = Error.Conflict("WhatsApp.FormAction.WindowClosed", "The customer service window is closed.");
    public static readonly Error InvalidMessage = Error.Validation("WhatsApp.FormAction.InvalidMessage", "The rendered message cannot be sent.");
    public static readonly Error DocumentUnavailable = Error.Conflict("WhatsApp.FormAction.DocumentUnavailable", "The private action document is unavailable.");
    public static readonly Error UploadFailed = Error.Conflict("WhatsApp.FormAction.UploadFailed", "The document could not be uploaded.");
    public static readonly Error SendRejected = Error.Conflict("WhatsApp.FormAction.SendRejected", "The provider rejected the message.");
    public static readonly Error StateConflict = Error.Conflict("WhatsApp.FormAction.StateConflict", "The action dispatch reservation changed.");
}
