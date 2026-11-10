using ArquitecturaBase.Domain.Modules.WhatsApp;

namespace ArquitecturaBase.Application.Modules.WhatsApp.Interfaces.Persistence;

public interface IWhatsAppSpreadsheetRepository
{
    /// <summary>The received spreadsheet, locked after its contact.</summary>
    Task<WhatsAppMessage?> GetForUpdateAsync(Guid contactId, Guid messageId, CancellationToken ct);
}
