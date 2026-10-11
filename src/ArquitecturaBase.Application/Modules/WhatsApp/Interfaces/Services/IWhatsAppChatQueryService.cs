using ArquitecturaBase.Application.Modules.WhatsApp.Models;
using ArquitecturaBase.Domain.Results;

namespace ArquitecturaBase.Application.Modules.WhatsApp.Interfaces.Services;

public interface IWhatsAppChatQueryService
{
    Task<Result<WhatsAppChatsResponse>> ListAsync(ListWhatsAppChatsRequest request, Guid actor, CancellationToken cancellationToken);
    Task<Result<WhatsAppThreadResponse>> GetThreadAsync(ListWhatsAppMessagesRequest request, Guid actor, CancellationToken cancellationToken);
    Task<Result<WhatsAppOutgoingMessageResponse>> FindOutgoingAsync(Guid contactId, Guid requestId, Guid actor, CancellationToken cancellationToken);
}
