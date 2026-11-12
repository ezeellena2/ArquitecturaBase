using ArquitecturaBase.Application.Modules.WhatsApp.Models;
using ArquitecturaBase.Domain.Results;

namespace ArquitecturaBase.Application.Modules.WhatsApp.Interfaces.Services;

/// <summary>
/// Probar (AS3): what the assistant would do with what is saved, as an account linked to WhatsApp and with its real history. It
/// runs in a channel of its own: nothing is queued, no limit of WhatsApp is spent, and the daily quota of the engine is the
/// tester's.
/// </summary>
public interface IWhatsAppAssistantAgentTestService
{
    Task<Result<WhatsAppAssistantTestRecipientsResponse>> ListRecipientsAsync(string? search, CancellationToken cancellationToken);
    Task<Result<WhatsAppAssistantAgentTestAcceptedResponse>> StartAsync(StartWhatsAppAssistantAgentTestRequest request, CancellationToken cancellationToken);
    Task<Result<WhatsAppAssistantAgentTestResponse>> GetAsync(Guid requestId, Guid contactId, CancellationToken cancellationToken);
}
