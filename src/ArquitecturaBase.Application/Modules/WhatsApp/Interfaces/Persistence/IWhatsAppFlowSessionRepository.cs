using ArquitecturaBase.Domain.Modules.WhatsApp;
namespace ArquitecturaBase.Application.Modules.WhatsApp.Interfaces.Persistence;
public interface IWhatsAppFlowSessionRepository
{
    Task<WhatsAppFlowSession?> GetAsync(Guid id, CancellationToken cancellationToken);
    void Add(WhatsAppFlowSession session);
}
