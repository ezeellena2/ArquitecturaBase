using ArquitecturaBase.Application.Modules.WhatsApp.Models;
using ArquitecturaBase.Domain.Results;
namespace ArquitecturaBase.Application.Modules.WhatsApp.Interfaces.Services;
public interface IWhatsAppFormSendService
{
    Task<Result<WhatsAppOutgoingMessageResponse>> SendAsync(SendWhatsAppFormRequest request, CancellationToken cancellationToken);
}
