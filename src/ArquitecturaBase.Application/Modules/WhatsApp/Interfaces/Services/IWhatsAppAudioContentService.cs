using ArquitecturaBase.Application.Modules.WhatsApp.Models;
using ArquitecturaBase.Domain.Results;
namespace ArquitecturaBase.Application.Modules.WhatsApp.Interfaces.Services;
public interface IWhatsAppAudioContentService
{
    Task<Result<WhatsAppAudioContentResponse>> OpenAsync(Guid contactId,Guid messageId,CancellationToken ct);
}
