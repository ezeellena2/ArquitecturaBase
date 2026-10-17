using ArquitecturaBase.Application.Models.Assistant;
using ArquitecturaBase.Application.Modules.WhatsApp.Models;
using ArquitecturaBase.Domain.Results;

namespace ArquitecturaBase.Application.Modules.WhatsApp.Interfaces.Services;

public interface IWhatsAppAudioDispatchService
{
    Task<Result<WhatsAppAssistantWorkResponse?>> PrepareAsync(Guid contactId, CancellationToken ct);
    Task<Result> CompleteAsync(Guid contactId, Guid requestId, Result<AssistantOperationResponse> outcome, CancellationToken ct);
}
