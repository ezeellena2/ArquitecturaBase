using ArquitecturaBase.Domain.Modules.WhatsApp;

namespace ArquitecturaBase.Application.Modules.WhatsApp.Interfaces.Persistence;

public interface IWhatsAppAssistantAgentRepository
{
    /// <summary>Serializes the saves of the main agent, also the first one, when there is no row to lock yet.</summary>
    Task LockAsync(CancellationToken cancellationToken);
    Task<WhatsAppAssistantAgent?> GetMainAsync(CancellationToken cancellationToken);
    void Add(WhatsAppAssistantAgent agent);
}
