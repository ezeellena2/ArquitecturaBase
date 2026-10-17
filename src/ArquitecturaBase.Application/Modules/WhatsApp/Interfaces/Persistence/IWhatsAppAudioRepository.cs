using ArquitecturaBase.Domain.Modules.WhatsApp;
namespace ArquitecturaBase.Application.Modules.WhatsApp.Interfaces.Persistence;
public interface IWhatsAppAudioRepository
{
    Task<WhatsAppMessage?> GetForUpdateAsync(Guid contactId, Guid messageId, CancellationToken ct);
    Task<WhatsAppMessage?> GetByRequestForUpdateAsync(Guid contactId, Guid requestId, CancellationToken ct);
}
