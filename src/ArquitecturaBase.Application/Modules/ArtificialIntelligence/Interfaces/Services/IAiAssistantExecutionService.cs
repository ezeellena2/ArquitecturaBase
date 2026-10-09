using ArquitecturaBase.Application.Modules.ArtificialIntelligence.Models;
using ArquitecturaBase.Domain.Results;

namespace ArquitecturaBase.Application.Modules.ArtificialIntelligence.Interfaces.Services;

public interface IAiAssistantExecutionService
{
    Task<Result<AiAssistantExecutionContextResponse>> PrepareAsync(AiAssistantClaimResponse claim, CancellationToken cancellationToken);
    Task<Result<LlmConversationResponse>> CallProviderAsync(AiAssistantExecutionContextResponse context, CancellationToken cancellationToken);
    Task<Result<AiAssistantAdvanceResponse>> AdvanceAsync(AiAssistantExecutionContextResponse context, LlmConversationResponse response, CancellationToken cancellationToken);
}
