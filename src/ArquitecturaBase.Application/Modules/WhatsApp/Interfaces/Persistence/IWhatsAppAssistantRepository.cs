using ArquitecturaBase.Domain.Modules.WhatsApp;

namespace ArquitecturaBase.Application.Modules.WhatsApp.Interfaces.Persistence;

public interface IWhatsAppAssistantRepository
{
    Task<WhatsAppAssistantRequest?> GetByRequestAsync(Guid requestId, CancellationToken ct);
    Task<WhatsAppAssistantRequest?> GetBySourceAsync(Guid contactId, Guid sourceMessageId, WhatsAppAssistantRequestKind kind, CancellationToken ct);
    Task<WhatsAppAssistantRequest?> GetByCapturedRecordAsync(Guid contactId, Guid recordId, CancellationToken ct);
    Task<WhatsAppAssistantRequest?> GetPendingAsync(Guid contactId, CancellationToken ct);
    /// <summary>Task 9c of the documents: the reference that sent a send's form (its Flow or its location request), whose reply a capture rejected.</summary>
    Task<WhatsAppAssistantRequest?> GetBySessionAsync(Guid contactId, Guid sessionId, CancellationToken ct);
    void Add(WhatsAppAssistantRequest request);
}
