using ArquitecturaBase.Application.Modules.WhatsApp.Models;
using ArquitecturaBase.Domain.Modules.WhatsApp;
namespace ArquitecturaBase.Application.Modules.WhatsApp.Interfaces.Persistence;
public interface IWhatsAppFlowSessionRepository
{
    Task<WhatsAppFlowSession?> GetAsync(Guid id, CancellationToken cancellationToken);
    /// <summary>W1-A: the contact's sends that still wait for their location, oldest first, each with the request that opened it.
    /// The caller holds the contact's lock.</summary>
    Task<IReadOnlyList<WhatsAppFlowLocationWait>> ListAwaitingLocationAsync(Guid contactId, DateTime nowUtc, CancellationToken cancellationToken);
    void Add(WhatsAppFlowSession session);
}
