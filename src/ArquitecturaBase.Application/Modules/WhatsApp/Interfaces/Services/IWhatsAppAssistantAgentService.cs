using ArquitecturaBase.Application.Modules.WhatsApp.Models;
using ArquitecturaBase.Domain.Results;

namespace ArquitecturaBase.Application.Modules.WhatsApp.Interfaces.Services;

/// <summary>Casos del asistente (AS1-A): read and save the main agent, with its revision and 409.</summary>
public interface IWhatsAppAssistantAgentService
{
    Task<Result<WhatsAppAssistantAgentResponse>> GetAsync(CancellationToken cancellationToken);
    Task<Result<WhatsAppAssistantAgentResponse>> UpdateAsync(UpdateWhatsAppAssistantAgentRequest request, CancellationToken cancellationToken);
}
