using ArquitecturaBase.Domain.Modules.WhatsApp;

namespace ArquitecturaBase.Application.Modules.WhatsApp.Interfaces.Persistence;

public interface IWhatsAppAssistantRepository
{
    Task<WhatsAppAssistantRequest?> GetByRequestAsync(Guid requestId, CancellationToken ct);
    Task<WhatsAppAssistantRequest?> GetBySourceAsync(Guid contactId, Guid sourceMessageId, WhatsAppAssistantRequestKind kind, CancellationToken ct);
    Task<WhatsAppAssistantRequest?> GetByCapturedRecordAsync(Guid contactId, Guid recordId, CancellationToken ct);
    Task<WhatsAppAssistantRequest?> GetPendingAsync(Guid contactId, CancellationToken ct);
    void Add(WhatsAppAssistantRequest request);
}
