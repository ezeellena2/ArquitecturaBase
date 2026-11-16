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
    /// <summary>The oldest pending turn, but for those the claim skipped because this version does not understand them (I4).</summary>
    Task<AiAssistantTurn?> GetPendingAsync(IReadOnlyCollection<Guid> skipped, CancellationToken cancellationToken);
    /// <summary>Returns the active actor/channel/conversation reservation; null preserves the unscoped group and audio normalization does not reserve it.</summary>
    Task<AiAssistantTurn?> GetActiveForActorChannelAsync(Guid ownerUserId, string channelId, Guid? channelConversationId, CancellationToken cancellationToken);
    Task<AiAssistantTurn?> GetActiveForConversationAsync(Guid conversationId, CancellationToken cancellationToken);
    Task<int> CountTurnsAsync(Guid conversationId, CancellationToken cancellationToken);
    /// <summary>The spreadsheet of the latest turn of the conversation that has one, own or inherited, even once its content was purged.</summary>
    Task<AiAssistantAttachmentRow?> FindLatestAttachmentAsync(Guid conversationId, CancellationToken cancellationToken);
    /// <summary>The turns of the person's conversation that named a recording or hold a file, with the key of its copy (I2).</summary>
    Task<IReadOnlyList<AiAssistantNamedSourceRow>> ListNamedSourcesAsync(Guid ownerUserId, Guid conversationId, CancellationToken cancellationToken);
    /// <summary>
    /// The day's acceptances and the active work. Document readings count apart: with <paramref name="isDocument"/>, the actor's in every
    /// channel and the installation's; without it, those of interpretations and audio (by channel and kind when both are given), never readings.
    /// A receipt whose turn expired without reserving a step does not count (I1).
    /// </summary>
    Task<AiAssistantBudgetCounts> CountBudgetAsync(Guid ownerUserId, DateOnly dayUtc, string? channelId, bool? isAudio, bool isDocument, CancellationToken cancellationToken);
    Task<IReadOnlyList<AiAssistantTurn>> ListExpiredAsync(DateTime nowUtc, int pendingSeconds, int graceSeconds, int limit, CancellationToken cancellationToken);
    /// <summary>
    /// Turns whose content goes: expired, of a removed conversation or of a deleted account, and a Home recording or reading submitted
    /// before <see cref="AiAssistantUnnamedSources.SubmittedBeforeUtc"/> that no turn of an active conversation named (I2).
    /// </summary>
    Task<IReadOnlyList<AiAssistantTurn>> ListPurgeableAsync(DateTime nowUtc, AiAssistantUnnamedSources unnamed, int limit, CancellationToken cancellationToken);
    /// <summary>The conversations a deleted account has not retired yet, with or without content left (I3).</summary>
    Task<IReadOnlyList<AiConversation>> ListDeletedOwnerConversationsAsync(int limit, CancellationToken cancellationToken);
    /// <summary>
    /// The conversations, of Home or another channel, without active turns, with all their content expired and no receipt still valid, even
    /// if nobody removed them (I3); a removed Home one does not wait for its receipts.
    /// </summary>
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
