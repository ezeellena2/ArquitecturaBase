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
    /// <summary>The private copies of files that a turn, its own or inherited, still has while its content lasts (Stage 2).</summary>
    Task<IReadOnlyList<string>> ListRetainedAttachmentKeysAsync(IReadOnlyCollection<string> attachmentKeys, CancellationToken cancellationToken);
    /// <summary>
    /// The requests of this channel whose operation can still be used (pending, running or completed) and keeps its content: a Home
    /// document reading keeps its copy while a turn can still take it (Stage 3).
    /// </summary>
    Task<IReadOnlyList<Guid>> ListRetainedRequestIdsAsync(string channelId, IReadOnlyCollection<Guid> requestIds, CancellationToken cancellationToken);
    /// <summary>The sealed spreadsheets of these turns of the owner, by turn: the rows never bring them, so only who uses one asks for it.</summary>
    Task<IReadOnlyDictionary<Guid, string>> ListProtectedAttachmentsAsync(Guid ownerUserId, IReadOnlyCollection<Guid> turnIds, CancellationToken cancellationToken);
}
