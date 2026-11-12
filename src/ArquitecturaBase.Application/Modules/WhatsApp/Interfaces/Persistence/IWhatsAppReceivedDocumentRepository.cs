using ArquitecturaBase.Domain.Modules.WhatsApp;

namespace ArquitecturaBase.Application.Modules.WhatsApp.Interfaces.Persistence;

public interface IWhatsAppReceivedDocumentRepository
{
    /// <summary>The received document, locked after its contact.</summary>
    Task<WhatsAppMessage?> GetForUpdateAsync(Guid contactId, Guid messageId, CancellationToken ct);

    /// <summary>The received document whose reading is this request, locked after its contact.</summary>
    Task<WhatsAppMessage?> GetByRequestForUpdateAsync(Guid contactId, Guid requestId, CancellationToken ct);
}
