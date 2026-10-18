using ArquitecturaBase.Application.Modules.ArtificialIntelligence.Interfaces.Persistence;
using ArquitecturaBase.Application.Modules.ArtificialIntelligence.Models;
using ArquitecturaBase.Domain.Modules.ArtificialIntelligence;
using ArquitecturaBase.Infrastructure.Persistence;
using ArquitecturaBase.Infrastructure.Persistence.Extensions;
using Microsoft.EntityFrameworkCore;

namespace ArquitecturaBase.Infrastructure.Modules.ArtificialIntelligence.Persistence.Repositories;

internal sealed class AiAssistantRepository(ApplicationDbContext dbContext, TimeProvider timeProvider) : IAiAssistantRepository
{
    public Task LockBudgetAsync(CancellationToken cancellationToken) =>
        dbContext.AcquireAdvisoryLocksAsync([AiAdvisoryLockKeys.AssistantBudget], cancellationToken);
    public Task LockActorAsync(Guid ownerUserId, CancellationToken cancellationToken) =>
        dbContext.AcquireAdvisoryLocksAsync([AiAdvisoryLockKeys.AssistantActor(ownerUserId)], cancellationToken);
    public Task LockConversationAsync(Guid conversationId, CancellationToken cancellationToken) =>
        dbContext.AcquireAdvisoryLocksAsync([AiAdvisoryLockKeys.AssistantConversation(conversationId)], cancellationToken);

    public Task<AiConversation?> GetConversationAsync(Guid ownerUserId, Guid conversationId, CancellationToken cancellationToken) =>
        dbContext.Set<AiConversation>().SingleOrDefaultAsync(conversation => conversation.Id == conversationId && conversation.OwnerUserId == ownerUserId, cancellationToken);
    public Task<AiAssistantTurn?> GetTurnAsync(Guid turnId, CancellationToken cancellationToken) =>
        dbContext.Set<AiAssistantTurn>().SingleOrDefaultAsync(turn => turn.Id == turnId, cancellationToken);
    public Task<AiAssistantRequestReceipt?> GetReceiptAsync(Guid ownerUserId, Guid requestId, CancellationToken cancellationToken)
    {
        var nowUtc = timeProvider.GetUtcNow().UtcDateTime;
        return dbContext.Set<AiAssistantRequestReceipt>().SingleOrDefaultAsync(receipt =>
            receipt.OwnerUserId == ownerUserId && receipt.RequestId == requestId && receipt.ExpiresAtUtc > nowUtc, cancellationToken);
    }
    public Task<AiAssistantTurn?> GetPendingAsync(CancellationToken cancellationToken) =>
        dbContext.Set<AiAssistantTurn>().Where(turn => turn.State == AiAssistantTurnState.Pending)
            .OrderBy(turn => turn.SubmittedAtUtc).ThenBy(turn => turn.Id).FirstOrDefaultAsync(cancellationToken);
    public Task<AiAssistantTurn?> GetActiveForActorChannelAsync(Guid ownerUserId, string channelId, CancellationToken cancellationToken) =>
        dbContext.Set<AiAssistantTurn>().IgnoreQueryFilters().FirstOrDefaultAsync(turn => turn.OwnerUserId == ownerUserId && turn.ChannelId == channelId
            && (turn.State == AiAssistantTurnState.Pending || turn.State == AiAssistantTurnState.Running), cancellationToken);
    public Task<AiAssistantTurn?> GetActiveForConversationAsync(Guid conversationId, CancellationToken cancellationToken) =>
        dbContext.Set<AiAssistantTurn>().FirstOrDefaultAsync(turn => turn.ConversationId == conversationId
            && (turn.State == AiAssistantTurnState.Pending || turn.State == AiAssistantTurnState.Running), cancellationToken);
    public Task<int> CountTurnsAsync(Guid conversationId, CancellationToken cancellationToken) =>
        dbContext.Set<AiAssistantTurn>().CountAsync(turn => turn.ConversationId == conversationId, cancellationToken);

    public async Task<AiAssistantBudgetCounts> CountBudgetAsync(Guid ownerUserId, DateOnly dayUtc, string? channelId, bool? isAudio, CancellationToken cancellationToken)
    {
        var receipts = dbContext.Set<AiAssistantRequestReceipt>().Where(receipt => receipt.AcceptedDayUtc == dayUtc);
        var actorReceipts = receipts.Where(receipt => receipt.OwnerUserId == ownerUserId);
        if (channelId is not null && isAudio is not null)
            actorReceipts = actorReceipts.Where(receipt => (receipt.ChannelId == null || receipt.ChannelId == channelId)
                && (receipt.IsAudio == null || receipt.IsAudio == isAudio));
        var active = dbContext.Set<AiAssistantTurn>().IgnoreQueryFilters();
        return new AiAssistantBudgetCounts(
            await actorReceipts.CountAsync(cancellationToken),
            await receipts.CountAsync(cancellationToken),
            await active.CountAsync(turn => turn.State == AiAssistantTurnState.Pending, cancellationToken),
            await active.CountAsync(turn => turn.State == AiAssistantTurnState.Running, cancellationToken));
    }

    public async Task<IReadOnlyList<AiAssistantTurn>> ListExpiredAsync(DateTime nowUtc, int pendingSeconds, int graceSeconds, int limit, CancellationToken cancellationToken)
    {
        var pendingCutoffUtc = nowUtc.AddSeconds(-pendingSeconds);
        var runningCutoffUtc = nowUtc.AddSeconds(-graceSeconds);
        return await dbContext.Set<AiAssistantTurn>().IgnoreQueryFilters().Where(turn =>
                (turn.State == AiAssistantTurnState.Pending && turn.SubmittedAtUtc <= pendingCutoffUtc)
                || (turn.State == AiAssistantTurnState.Running && turn.DeadlineAtUtc <= runningCutoffUtc))
            .OrderBy(turn => turn.SubmittedAtUtc).ThenBy(turn => turn.Id).Take(limit).ToListAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<AiAssistantTurn>> ListPurgeableAsync(DateTime nowUtc, int limit, CancellationToken cancellationToken) =>
        await dbContext.Set<AiAssistantTurn>().IgnoreQueryFilters().Where(turn => turn.ProtectedQuestion != null
            && (turn.ContentExpiresAtUtc <= nowUtc || turn.Conversation.State == AiConversationState.Removed
                || dbContext.Users.IgnoreQueryFilters().Any(user => user.Id == turn.OwnerUserId && user.IsDeleted)))
            .OrderBy(turn => turn.ContentExpiresAtUtc).ThenBy(turn => turn.Id).Take(limit).ToListAsync(cancellationToken);

    public async Task<IReadOnlyList<AiAssistantRequestReceipt>> ListExpiredReceiptsAsync(DateTime nowUtc, int limit, CancellationToken cancellationToken) =>
        await dbContext.Set<AiAssistantRequestReceipt>().Where(receipt => receipt.ExpiresAtUtc <= nowUtc)
            .OrderBy(receipt => receipt.ExpiresAtUtc).ThenBy(receipt => receipt.Id).Take(limit).ToListAsync(cancellationToken);
    public async Task<IReadOnlyList<AiConversation>> ListRemovableConversationsAsync(DateTime nowUtc, int limit, CancellationToken cancellationToken) =>
        await Removable(nowUtc).OrderBy(conversation => conversation.RemovalRequestedAtUtc).ThenBy(conversation => conversation.Id)
            .Take(limit).ToListAsync(cancellationToken);
    private IQueryable<AiConversation> Removable(DateTime nowUtc) =>
        dbContext.Set<AiConversation>().IgnoreQueryFilters().Where(conversation =>
            (conversation.State == AiConversationState.Removed || conversation.ChannelId != "home")
            && (conversation.ChannelId == "home" || !dbContext.Set<AiAssistantRequestReceipt>().Any(receipt =>
                receipt.ConversationId == conversation.Id && receipt.ExpiresAtUtc > nowUtc))
            && !dbContext.Set<AiAssistantTurn>().IgnoreQueryFilters().Any(turn => turn.ConversationId == conversation.Id
                && (turn.ContentExpiresAtUtc > nowUtc || turn.State == AiAssistantTurnState.Pending || turn.State == AiAssistantTurnState.Running)));
    public async Task<IReadOnlyList<AiAssistantToolExecution>> ListToolsAsync(Guid turnId, CancellationToken cancellationToken) =>
        await dbContext.Set<AiAssistantToolExecution>().Where(tool => tool.TurnId == turnId)
            .OrderBy(tool => tool.Ordinal).ToListAsync(cancellationToken);

    public void AddConversation(AiConversation conversation) { dbContext.RequireTransaction(); dbContext.Set<AiConversation>().Add(conversation); }
    public void AddTurn(AiAssistantTurn turn) { dbContext.RequireTransaction(); dbContext.Set<AiAssistantTurn>().Add(turn); }
    public void AddReceipt(AiAssistantRequestReceipt receipt) { dbContext.RequireTransaction(); dbContext.Set<AiAssistantRequestReceipt>().Add(receipt); }
    public void AddTool(AiAssistantToolExecution execution) { dbContext.RequireTransaction(); dbContext.Set<AiAssistantToolExecution>().Add(execution); }
    public void RemoveTool(AiAssistantToolExecution execution) { dbContext.RequireTransaction(); dbContext.Set<AiAssistantToolExecution>().Remove(execution); }
    public void RemoveReceipt(AiAssistantRequestReceipt receipt) { dbContext.RequireTransaction(); dbContext.Set<AiAssistantRequestReceipt>().Remove(receipt); }
    public void RemoveConversation(AiConversation conversation) { dbContext.RequireTransaction(); dbContext.Set<AiConversation>().Remove(conversation); }
}
