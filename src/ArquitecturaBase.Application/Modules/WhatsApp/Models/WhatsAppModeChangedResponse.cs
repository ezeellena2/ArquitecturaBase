using ArquitecturaBase.Domain.Modules.WhatsApp;

namespace ArquitecturaBase.Application.Modules.WhatsApp.Models;

public sealed record WhatsAppModeChangedResponse(WhatsAppConversationMode Mode, long ModeRevision, bool AssistantSendInProgress);
