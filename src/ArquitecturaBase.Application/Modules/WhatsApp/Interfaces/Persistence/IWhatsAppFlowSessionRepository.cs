using ArquitecturaBase.Domain.Modules.WhatsApp;
namespace ArquitecturaBase.Application.Modules.WhatsApp.Interfaces.Persistence;
public interface IWhatsAppFlowSessionRepository
{
    Task<WhatsAppFlowSession?> GetAsync(Guid id, CancellationToken cancellationToken);
    /// <summary>W1-A: the contact's sends that still wait for their location, oldest first. The caller holds the contact's lock.</summary>
    Task<IReadOnlyList<WhatsAppFlowSession>> ListAwaitingLocationAsync(Guid contactId, DateTime nowUtc, CancellationToken cancellationToken);
    void Add(WhatsAppFlowSession session);
}
