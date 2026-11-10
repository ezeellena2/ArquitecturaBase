using ArquitecturaBase.Application.Common.Pagination;
using ArquitecturaBase.Application.Modules.ArtificialIntelligence.Models;

namespace ArquitecturaBase.Application.Modules.ArtificialIntelligence.Interfaces.Persistence;

public interface IAiAssistantReader
{
    Task<AiAssistantWorkloadRow> FindWorkloadAsync(DateTime nowUtc, int leaseGraceSeconds, CancellationToken cancellationToken);
    Task<AiConversationRow?> FindConversationAsync(Guid ownerUserId, Guid conversationId, CancellationToken cancellationToken);
    Task<AiAssistantTurnRow?> FindTurnAsync(Guid turnId, CancellationToken cancellationToken);
    Task<AiAssistantTurnRow?> FindTurnByRequestAsync(Guid ownerUserId, Guid requestId, CancellationToken cancellationToken);
    Task<PagedResult<AiConversationRow>> ListConversationsAsync(Guid ownerUserId, ListAiConversationsRequest request, CancellationToken cancellationToken);
    Task<PagedResult<AiAssistantTurnRow>> ListTurnsAsync(Guid ownerUserId, Guid conversationId, ListAiAssistantTurnsRequest request, CancellationToken cancellationToken);
    /// <summary>The Home recordings that a turn still names while its content lasts (IA4), by the request that normalized them.</summary>
    Task<IReadOnlyList<Guid>> ListRetainedAudioRequestIdsAsync(IReadOnlyCollection<Guid> audioRequestIds, CancellationToken cancellationToken);
    /// <summary>The private copies of spreadsheets that a turn, its own or inherited, still has while its content lasts (Stage 2).</summary>
    Task<IReadOnlyList<string>> ListRetainedAttachmentKeysAsync(IReadOnlyCollection<string> attachmentKeys, CancellationToken cancellationToken);
}
