using ArquitecturaBase.Application.Modules.WhatsApp.Models;
using ArquitecturaBase.Domain.Results;

namespace ArquitecturaBase.Application.Modules.WhatsApp.Interfaces.Services;

public interface IWhatsAppChatModeService
{
    Task<Result<WhatsAppModeChangedResponse>> ChangeAsync(ChangeWhatsAppModeRequest request, CancellationToken cancellationToken);
}
