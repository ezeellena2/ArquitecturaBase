using ArquitecturaBase.Domain.Results;

namespace ArquitecturaBase.Domain.Modules.WhatsApp;

public static class WhatsAppAssistantAgentErrors
{
    public static readonly Error Invalid = Error.Validation("WhatsApp.AssistantAgent.Invalid", "The assistant agent configuration is invalid.");
    public static readonly Error RevisionConflict = Error.Conflict("WhatsApp.AssistantAgent.RevisionConflict", "Another save changed the assistant agent first.");
}
