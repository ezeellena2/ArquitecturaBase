using ArquitecturaBase.Application.Models.Assistant;

namespace ArquitecturaBase.Application.Modules.WhatsApp.Models;

public sealed record WhatsAppAssistantNoReplyPayload(string Reason) : AssistantCapabilityPayload;
public sealed record WhatsAppAssistantDeclinePayload(int ReadOrdinal, Guid RequestId, Guid DefinitionId) : AssistantCapabilityPayload;
