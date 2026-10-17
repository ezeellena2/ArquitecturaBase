using ArquitecturaBase.Application.Modules.WhatsApp.Models;
using ArquitecturaBase.Domain.Results;

namespace ArquitecturaBase.Application.Modules.WhatsApp.Interfaces.Services;

public interface IWhatsAppAssistantService
{
    Task<Result<WhatsAppAssistantSuggestionResponse>> RequestAsync(RequestWhatsAppAssistantSuggestionRequest request, CancellationToken ct);
    Task<Result<WhatsAppAssistantSuggestionResponse>> GetAsync(Guid contactId, Guid requestId, CancellationToken ct);
    Task<Result<WhatsAppAssistantSuggestionResponse>> DiscardAsync(Guid contactId, Guid requestId, CancellationToken ct);
    Task<Result<WhatsAppOutgoingMessageResponse>> SendAsync(SendWhatsAppAssistantSuggestionRequest request, CancellationToken ct);
}
