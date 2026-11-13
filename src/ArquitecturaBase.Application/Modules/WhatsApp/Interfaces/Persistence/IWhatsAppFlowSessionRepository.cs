using ArquitecturaBase.Application.Modules.WhatsApp.Models;
using ArquitecturaBase.Domain.Modules.WhatsApp;
namespace ArquitecturaBase.Application.Modules.WhatsApp.Interfaces.Persistence;
public interface IWhatsAppFlowSessionRepository
{
    Task<WhatsAppFlowSession?> GetAsync(Guid id, CancellationToken cancellationToken);
    /// <summary>W1-A: the contact's sends that still wait for their location, oldest first, each with the request that opened it.
    /// The caller holds the contact's lock.</summary>
    Task<IReadOnlyList<WhatsAppFlowLocationWait>> ListAwaitingLocationAsync(Guid contactId, DateTime nowUtc, CancellationToken cancellationToken);
    /// <summary>W3: the contact's sends of the account and link it has today, without a reply (captured or attached) and not expired,
    /// whose Flow went out (Meta accepted it, or a person reviewed it as sent), oldest first and up to 50, each with its first message and
    /// its sealed projection. The caller holds the contact's lock.</summary>
    Task<IReadOnlyList<WhatsAppFlowOpenSend>> ListOpenAsync(Guid contactId, DateTime nowUtc, CancellationToken cancellationToken);
    void Add(WhatsAppFlowSession session);
}
