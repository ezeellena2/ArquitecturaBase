using ArquitecturaBase.Application.Modules.WhatsApp.Models;
namespace ArquitecturaBase.Application.Modules.WhatsApp.Interfaces.Integrations;
public interface IWhatsAppFlowSessionProtector
{
    byte[] Protect(Guid sessionId, Guid contactId, Guid userId, Guid versionId, WhatsAppFlowSessionPayload payload);
    WhatsAppFlowSessionPayload Read(Guid sessionId, Guid contactId, Guid userId, Guid versionId, byte[] payload);
}
