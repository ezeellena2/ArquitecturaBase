using ArquitecturaBase.Application.Modules.ArtificialIntelligence.Models;
using ArquitecturaBase.Domain.Results;

namespace ArquitecturaBase.Application.Modules.ArtificialIntelligence.Interfaces.Services;

public interface IAiAssistantDispatchService
{
    Task<Result<AiAssistantClaimResponse?>> ClaimAsync(CancellationToken cancellationToken);
    Task<Result<bool>> BeginProviderStepAsync(AiAssistantClaimResponse claim, CancellationToken cancellationToken);
    Task<Result> RecordProgressAsync(AiAssistantClaimResponse claim, AiAssistantAdvanceResponse progress, CancellationToken cancellationToken);
    Task<Result> CompleteAsync(AiAssistantClaimResponse claim, AiAssistantAdvanceResponse completion, CancellationToken cancellationToken);
    Task<Result> FailAsync(AiAssistantClaimResponse claim, string failureCode, bool uncertain, CancellationToken cancellationToken, LlmConversationUsage? usage = null);
}
