using ArquitecturaBase.Application.Modules.WhatsApp.Models;

namespace ArquitecturaBase.Application.Modules.WhatsApp.Interfaces.Persistence;

public interface IWhatsAppFormRecipientReader
{
    Task<WhatsAppFormRecipientRow?> FindAsync(Guid contactId, CancellationToken cancellationToken);
}
