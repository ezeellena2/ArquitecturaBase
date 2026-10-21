namespace ArquitecturaBase.Application.Modules.WhatsApp.Models;

public enum WhatsAppFlowRemoteState { Unknown, Published, Deprecated }

public sealed record WhatsAppFlowStateResponse(bool IdentityMatches, WhatsAppFlowRemoteState State);
