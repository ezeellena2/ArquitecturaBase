using ArquitecturaBase.Application.Modules.WhatsApp.Models;

namespace ArquitecturaBase.Application.Modules.WhatsApp.Interfaces.Persistence;

/// <summary>
/// Casos del asistente: the history the assistant reads, always of one conversation and never of another. Only reads, bounded
/// in SQL.
/// </summary>
public interface IWhatsAppAssistantHistoryReader
{
    /// <summary>The chat the channel names when it is linked to the subject, or else the one linked to the subject's account.</summary>
    Task<Guid?> FindConversationAsync(Guid subjectUserId, Guid? contactId, CancellationToken cancellationToken);

    /// <summary>Up to <paramref name="limit"/> messages since <paramref name="sinceUtc"/>, oldest first, without access events.</summary>
    Task<IReadOnlyList<WhatsAppAssistantHistoryMessageRow>> ListMessagesAsync(Guid contactId, DateTime sinceUtc, int limit, CancellationToken cancellationToken);

    /// <summary>Up to <paramref name="limit"/> messages of the assistant since <paramref name="sinceUtc"/>, oldest first, without failed ones.</summary>
    Task<IReadOnlyList<WhatsAppAssistantHistoryActionRow>> ListActionsAsync(Guid contactId, DateTime sinceUtc, int limit, CancellationToken cancellationToken);

    /// <summary>The replies of the cases (texts and questions) since <paramref name="sinceUtc"/>, for the agent's daily limit.</summary>
    Task<int> CountRepliesAsync(Guid contactId, DateTime sinceUtc, CancellationToken cancellationToken);

    /// <summary>
    /// Ráfagas: when the person's current burst started, the first business message after what was last answered or sent; null
    /// without one.
    /// </summary>
    Task<DateTime?> FindBurstStartAsync(Guid contactId, CancellationToken cancellationToken);
}
