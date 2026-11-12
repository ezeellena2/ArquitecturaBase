using ArquitecturaBase.Application.Modules.WhatsApp.Interfaces.Persistence;
using ArquitecturaBase.Application.Modules.WhatsApp.Models;

namespace ArquitecturaBase.Application.UnitTests.TestFeatures.Modules.WhatsApp;

/// <summary>The saved agent, or none: without it the assistant does what it did before Casos del asistente.</summary>
internal sealed class FakeWhatsAppAssistantAgentReader : IWhatsAppAssistantAgentReader
{
    public WhatsAppAssistantAgentRow? Agent { get; set; }

    public Task<WhatsAppAssistantAgentRow?> FindMainAsync(CancellationToken cancellationToken) => Task.FromResult(Agent);

    public List<WhatsAppAssistantTestRecipientRow> Recipients { get; } = [];
    public (string? Search, int Limit)? Listed { get; private set; }

    public Task<IReadOnlyList<WhatsAppAssistantTestRecipientRow>> ListTestRecipientsAsync(string? search, int limit, CancellationToken cancellationToken)
    {
        Listed = (search, limit);
        return Task.FromResult<IReadOnlyList<WhatsAppAssistantTestRecipientRow>>([.. Recipients.Take(limit)]);
    }
}

/// <summary>A conversation's history with fictitious rows and the replies already used today.</summary>
internal sealed class FakeWhatsAppAssistantHistoryReader : IWhatsAppAssistantHistoryReader
{
    public Guid? Contact { get; set; }
    public List<WhatsAppAssistantHistoryMessageRow> Messages { get; } = [];
    public List<WhatsAppAssistantHistoryActionRow> Actions { get; } = [];
    public int Replies { get; set; }
    public (Guid ContactId, DateTime SinceUtc)? CountedReplies { get; private set; }

    public Task<Guid?> FindConversationAsync(Guid subjectUserId, Guid? contactId, CancellationToken cancellationToken) => Task.FromResult(Contact ?? contactId);

    public Task<IReadOnlyList<WhatsAppAssistantHistoryMessageRow>> ListMessagesAsync(Guid contactId, DateTime sinceUtc, int limit, CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<WhatsAppAssistantHistoryMessageRow>>([.. Messages.TakeLast(limit)]);

    public Task<IReadOnlyList<WhatsAppAssistantHistoryActionRow>> ListActionsAsync(Guid contactId, DateTime sinceUtc, int limit, CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<WhatsAppAssistantHistoryActionRow>>([.. Actions.TakeLast(limit)]);

    public Task<int> CountRepliesAsync(Guid contactId, DateTime sinceUtc, CancellationToken cancellationToken)
    {
        CountedReplies = (contactId, sinceUtc);
        return Task.FromResult(Replies);
    }

    public DateTime? BurstStartUtc { get; set; }

    public Task<DateTime?> FindBurstStartAsync(Guid contactId, CancellationToken cancellationToken) => Task.FromResult(BurstStartUtc);
}
