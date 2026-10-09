using ArquitecturaBase.Application.Modules.WhatsApp.Models;
using ArquitecturaBase.Application.Common.Pagination;

namespace ArquitecturaBase.Application.Modules.WhatsApp.Interfaces.Persistence;

public interface IWhatsAppChatReader
{
    Task<PagedResult<WhatsAppChatRow>> ListChatsAsync(ListWhatsAppChatsRequest request, Guid operatorUserId, CancellationToken cancellationToken);
    Task<WhatsAppChatCounts> CountChatsAsync(ListWhatsAppChatsRequest request, Guid operatorUserId, CancellationToken cancellationToken);
    Task<WhatsAppThreadRow?> FindThreadAsync(ListWhatsAppMessagesRequest request, Guid operatorUserId, CancellationToken cancellationToken);
    Task<WhatsAppOutgoingMessageRow?> FindOutgoingAsync(Guid contactId, Guid requestId, Guid operatorUserId, CancellationToken cancellationToken);
}
