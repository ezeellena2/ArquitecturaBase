using ArquitecturaBase.Application.Common.Pagination;
using ArquitecturaBase.Application.Modules.ArtificialIntelligence.Models;
using ArquitecturaBase.Domain.Results;

namespace ArquitecturaBase.Application.Modules.ArtificialIntelligence.Interfaces.Services;

public interface IAiAssistantService
{
    Task<Result<AiAssistantBootstrapResponse>> BootstrapAsync(CancellationToken cancellationToken);
    Task<Result<PagedResult<AiConversationSummaryResponse>>> ListConversationsAsync(ListAiConversationsRequest request, CancellationToken cancellationToken);
    Task<Result<AiConversationResponse>> GetConversationAsync(Guid conversationId, CancellationToken cancellationToken);
    Task<Result<PagedResult<AiAssistantTurnResponse>>> ListTurnsAsync(Guid conversationId, ListAiAssistantTurnsRequest request, CancellationToken cancellationToken);
    Task<Result<AiAssistantTurnResponse>> GetTurnAsync(Guid requestId, CancellationToken cancellationToken);
    Task<Result<AiAssistantTurnAcceptedResponse>> SubmitAsync(SubmitAiAssistantTurnRequest request, CancellationToken cancellationToken);
    Task<Result<AiAssistantTurnResponse>> CancelAsync(CancelAiAssistantTurnRequest request, CancellationToken cancellationToken);
    Task<Result> RemoveConversationAsync(Guid conversationId, CancellationToken cancellationToken);
}
