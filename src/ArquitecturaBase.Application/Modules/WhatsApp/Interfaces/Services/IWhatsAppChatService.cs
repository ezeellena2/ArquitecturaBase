using ArquitecturaBase.Application.Modules.WhatsApp.Models;
using ArquitecturaBase.Domain.Results;

namespace ArquitecturaBase.Application.Modules.WhatsApp.Interfaces.Services;

public interface IWhatsAppChatService
{
    Task<Result<WhatsAppChatsResponse>> ListAsync(ListWhatsAppChatsRequest request, CancellationToken cancellationToken);
    Task<Result<WhatsAppThreadResponse>> GetThreadAsync(ListWhatsAppMessagesRequest request, CancellationToken cancellationToken);
    Task<Result<WhatsAppOutgoingMessageResponse>> FindOutgoingAsync(Guid contactId, Guid requestId, CancellationToken cancellationToken);
    Task<Result<WhatsAppOutgoingMessageResponse>> SendTextAsync(SendWhatsAppTextRequest request, CancellationToken cancellationToken);
    Task<Result> MarkReadAsync(MarkWhatsAppChatReadRequest request, CancellationToken cancellationToken);
    Task<Result> ReviewUncertainAsync(Guid contactId, Guid requestId, CancellationToken cancellationToken);

    /// <summary>
    /// H2: an uncertain send of the chat, by its id, reviewed by any operator who can send, because the <c>requestId</c> belongs to
    /// whoever sent it. It does not send it again nor say that it arrived.
    /// </summary>
    Task<Result> ReviewUncertainOutgoingAsync(Guid contactId, Guid outgoingId, CancellationToken cancellationToken);
}
