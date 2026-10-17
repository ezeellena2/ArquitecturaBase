using ArquitecturaBase.Application.Modules.WhatsApp.Models;
using ArquitecturaBase.Domain.Results;
namespace ArquitecturaBase.Application.Modules.WhatsApp.Interfaces.Services;
public interface IWhatsAppAudioService
{
    Task<Result<WhatsAppAudioResponse>> RequestAsync(Guid contactId,Guid messageId,CancellationToken ct);
}
