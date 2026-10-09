namespace ArquitecturaBase.Domain.Modules.WhatsApp;

public enum WhatsAppOutgoingMessageState
{
    Pending,
    Sending,
    Accepted,
    Failed,
    Uncertain,
    Reviewed,
}
