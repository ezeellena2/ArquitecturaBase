using ArquitecturaBase.Application.Modules.ArtificialIntelligence.Models;
using ArquitecturaBase.Domain.Modules.ArtificialIntelligence;

namespace ArquitecturaBase.Application.Modules.ArtificialIntelligence.Interfaces.Persistence;

public interface IAiAssistantRepository
{
    Task LockBudgetAsync(CancellationToken cancellationToken);
    Task LockActorAsync(Guid ownerUserId, CancellationToken cancellationToken);
    Task LockConversationAsync(Guid conversationId, CancellationToken cancellationToken);
    Task<AiConversation?> GetConversationAsync(Guid ownerUserId, Guid conversationId, CancellationToken cancellationToken);
    Task<AiAssistantTurn?> GetTurnAsync(Guid turnId, CancellationToken cancellationToken);
    Task<AiAssistantRequestReceipt?> GetReceiptAsync(Guid ownerUserId, Guid requestId, CancellationToken cancellationToken);
    Task<AiAssistantTurn?> GetPendingAsync(CancellationToken cancellationToken);
    Task<AiAssistantTurn?> GetActiveForActorChannelAsync(Guid ownerUserId, string channelId, CancellationToken cancellationToken);
    Task<AiAssistantTurn?> GetActiveForConversationAsync(Guid conversationId, CancellationToken cancellationToken);
    Task<int> CountTurnsAsync(Guid conversationId, CancellationToken cancellationToken);
    Task<AiAssistantBudgetCounts> CountBudgetAsync(Guid ownerUserId, DateOnly dayUtc, CancellationToken cancellationToken);
    Task<IReadOnlyList<AiAssistantTurn>> ListExpiredAsync(DateTime nowUtc, int pendingSeconds, int graceSeconds, int limit, CancellationToken cancellationToken);
    Task<IReadOnlyList<AiAssistantTurn>> ListPurgeableAsync(DateTime nowUtc, int limit, CancellationToken cancellationToken);
    Task<IReadOnlyList<AiConversation>> ListRemovableConversationsAsync(DateTime nowUtc, int limit, CancellationToken cancellationToken);
    Task<IReadOnlyList<AiAssistantRequestReceipt>> ListExpiredReceiptsAsync(DateTime nowUtc, int limit, CancellationToken cancellationToken);
    Task<IReadOnlyList<AiAssistantToolExecution>> ListToolsAsync(Guid turnId, CancellationToken cancellationToken);
    void AddConversation(AiConversation conversation);
    void AddTurn(AiAssistantTurn turn);
    void AddReceipt(AiAssistantRequestReceipt receipt);
    void AddTool(AiAssistantToolExecution execution);
    void RemoveTool(AiAssistantToolExecution execution);
    void RemoveReceipt(AiAssistantRequestReceipt receipt);
    void RemoveConversation(AiConversation conversation);
}
