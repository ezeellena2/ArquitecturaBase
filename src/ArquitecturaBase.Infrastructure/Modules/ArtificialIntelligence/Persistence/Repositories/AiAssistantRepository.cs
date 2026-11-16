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
    public async Task<IReadOnlyList<AiAssistantTurn>> ListPendingAsync(int limit, CancellationToken cancellationToken) =>
        await dbContext.Set<AiAssistantTurn>().Where(turn => turn.State == AiAssistantTurnState.Pending)
            .OrderBy(turn => turn.SubmittedAtUtc).ThenBy(turn => turn.Id).Take(limit).ToListAsync(cancellationToken);
    public Task<AiAssistantTurn?> GetActiveForActorChannelAsync(Guid ownerUserId, string channelId, Guid? channelConversationId, CancellationToken cancellationToken) =>
        ActiveForActorChannel(ownerUserId, channelId, channelConversationId).FirstOrDefaultAsync(cancellationToken);
    private IQueryable<AiAssistantTurn> ActiveForActorChannel(Guid ownerUserId, string channelId, Guid? channelConversationId) =>
        // Like the index: the technical purposes (an audio 1, a reading 2) never take the slot; an absent purpose keeps it.
        dbContext.Set<AiAssistantTurn>().FromSqlRaw("""
            SELECT * FROM "AiAssistantTurns"
            WHERE (CASE WHEN "ExecutionProfileJson" IS JSON OBJECT THEN "ExecutionProfileJson"::jsonb -> 'purpose' END) IS NULL OR
                (CASE WHEN "ExecutionProfileJson" IS JSON OBJECT THEN "ExecutionProfileJson"::jsonb -> 'purpose' END) NOT IN ('1'::jsonb, '2'::jsonb)
            """).IgnoreQueryFilters().Where(turn => turn.OwnerUserId == ownerUserId && turn.ChannelId == channelId
            && turn.ChannelConversationId == channelConversationId
            && (turn.State == AiAssistantTurnState.Pending || turn.State == AiAssistantTurnState.Running));
    public Task<AiAssistantTurn?> GetActiveForConversationAsync(Guid conversationId, CancellationToken cancellationToken) =>
        dbContext.Set<AiAssistantTurn>().FirstOrDefaultAsync(turn => turn.ConversationId == conversationId
            && (turn.State == AiAssistantTurnState.Pending || turn.State == AiAssistantTurnState.Running), cancellationToken);
    public Task<int> CountTurnsAsync(Guid conversationId, CancellationToken cancellationToken) =>
        dbContext.Set<AiAssistantTurn>().CountAsync(turn => turn.ConversationId == conversationId, cancellationToken);
    // A projection, never tracked: only what inheriting needs, under the lock of the conversation.
    public Task<AiAssistantAttachmentRow?> FindLatestAttachmentAsync(Guid conversationId, CancellationToken cancellationToken) =>
        dbContext.Set<AiAssistantTurn>().Where(turn => turn.ConversationId == conversationId && turn.AttachmentKey != null)
            .OrderByDescending(turn => turn.Ordinal)
            .Select(turn => new AiAssistantAttachmentRow(turn.Id, turn.PayloadRevision, turn.ContentExpiresAtUtc, turn.AttachmentKey!, turn.ProtectedAttachment))
            .FirstOrDefaultAsync(cancellationToken);
    // I2: a projection of the conversation's turns that named a recording or hold a copy, whose key names a Home reading's request.
    public async Task<IReadOnlyList<AiAssistantNamedSourceRow>> ListNamedSourcesAsync(Guid ownerUserId, Guid conversationId, CancellationToken cancellationToken) =>
        await dbContext.Set<AiAssistantTurn>().Where(turn => turn.ConversationId == conversationId && turn.OwnerUserId == ownerUserId
                && (turn.AudioRequestId != null || turn.AttachmentKey != null))
            .Select(turn => new AiAssistantNamedSourceRow(turn.AudioRequestId, turn.AttachmentKey))
            .ToArrayAsync(cancellationToken);

    public async Task<AiAssistantBudgetCounts> CountBudgetAsync(Guid ownerUserId, DateOnly dayUtc, string? channelId, bool? isAudio, bool isDocument,
        CancellationToken cancellationToken)
    {
        // Readings have their own quotas: the actor's in every channel and the installation's; the others never count them.
        // I1: what expired without reserving a step (Expired, which a turn only reaches with none) gives its quota back, and its receipt
        // still deduplicates. Only that terminal state of its turn, kept after the purge, is looked at; a turn already deleted counts.
        var receipts = dbContext.Set<AiAssistantRequestReceipt>().Where(receipt => receipt.AcceptedDayUtc == dayUtc && receipt.IsDocument == isDocument
            && !dbContext.Set<AiAssistantTurn>().IgnoreQueryFilters().Any(turn => turn.Id == receipt.TurnId && turn.State == AiAssistantTurnState.Expired));
        var actorReceipts = receipts.Where(receipt => receipt.OwnerUserId == ownerUserId);
        if (!isDocument && channelId is not null && isAudio is not null)
            actorReceipts = actorReceipts.Where(receipt => (receipt.ChannelId == null || receipt.ChannelId == channelId)
                && (receipt.IsAudio == null || receipt.IsAudio == isAudio));
        var active = dbContext.Set<AiAssistantTurn>().IgnoreQueryFilters();
        return new AiAssistantBudgetCounts(
            await actorReceipts.CountAsync(cancellationToken),
            await receipts.CountAsync(cancellationToken),
            await active.CountAsync(turn => turn.State == AiAssistantTurnState.Pending, cancellationToken),
            await active.CountAsync(turn => turn.State == AiAssistantTurnState.Running, cancellationToken));
    }

    // I19: the claim's capacity, which the index by state answers, without the day's receipts.
    public Task<int> CountRunningAsync(CancellationToken cancellationToken) =>
        dbContext.Set<AiAssistantTurn>().IgnoreQueryFilters().CountAsync(turn => turn.State == AiAssistantTurnState.Running, cancellationToken);

    public async Task<IReadOnlyList<AiAssistantTurn>> ListExpiredAsync(DateTime nowUtc, int pendingSeconds, int graceSeconds, int limit, CancellationToken cancellationToken)
    {
        var pendingCutoffUtc = nowUtc.AddSeconds(-pendingSeconds);
        var runningCutoffUtc = nowUtc.AddSeconds(-graceSeconds);
        return await dbContext.Set<AiAssistantTurn>().IgnoreQueryFilters().Where(turn =>
                (turn.State == AiAssistantTurnState.Pending && turn.SubmittedAtUtc <= pendingCutoffUtc)
                || (turn.State == AiAssistantTurnState.Running && turn.DeadlineAtUtc <= runningCutoffUtc))
            .OrderBy(turn => turn.SubmittedAtUtc).ThenBy(turn => turn.Id).Take(limit).ToListAsync(cancellationToken);
    }

    // I2: a Home recording or reading that no turn of an active conversation named goes once it is old enough, like an orphan copy: a turn
    // names a recording by its request and holds the copy of a reading, whose key names the reading's request. IgnoreQueryFilters applies
    // to the whole query, the naming turns too, so they ask for an active conversation themselves: a purged turn of a removed one keeps its
    // AudioRequestId and AttachmentKey. It runs under the budget lock, so each naming lookup is an equality that a filtered index answers:
    // AudioRequestId, or AttachmentKey with one whole key per extension. Only technical turns past the cut get there.
    // I20: one condition with every reason read the whole history. Each reason is its own query over the turns that still have content (a
    // filtered index), with the batch's order and limit; the repository joins their Ids and the final query sorts them and cuts the limit:
    // the same turns as the one condition.
    public async Task<IReadOnlyList<AiAssistantTurn>> ListPurgeableAsync(DateTime nowUtc, AiAssistantUnnamedSources unnamed, int limit,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(unnamed);
        var naming = dbContext.Set<AiAssistantTurn>();
        var extensions = unnamed.DocumentKeyExtensions.ToArray();
        var content = dbContext.Set<AiAssistantTurn>().IgnoreQueryFilters().Where(turn => turn.ProtectedQuestion != null);
        var unnamedBefore = content.Where(turn => turn.SubmittedAtUtc <= unnamed.SubmittedBeforeUtc);
        IQueryable<AiAssistantTurn>[] reasons =
        [
            content.Where(turn => turn.ContentExpiresAtUtc <= nowUtc),
            content.Where(turn => turn.Conversation.State == AiConversationState.Removed),
            content.Where(turn => dbContext.Users.IgnoreQueryFilters().Any(user => user.Id == turn.OwnerUserId && user.IsDeleted)),
            unnamedBefore.Where(turn => turn.ChannelId == unnamed.AudioChannelId && !naming.Any(named => named.OwnerUserId == turn.OwnerUserId
                && named.Conversation.State == AiConversationState.Active && named.AudioRequestId == turn.RequestId)),
            unnamedBefore.Where(turn => turn.ChannelId == unnamed.DocumentChannelId && !extensions.Any(extension => naming.Any(named =>
                named.AttachmentKey == unnamed.DocumentKeyPrefix + turn.RequestId.ToString().Replace("-", "") + extension
                && named.OwnerUserId == turn.OwnerUserId && named.Conversation.State == AiConversationState.Active))),
        ];
        var ids = new HashSet<Guid>();
        foreach (var reason in reasons)
            ids.UnionWith(await reason.OrderBy(turn => turn.ContentExpiresAtUtc).ThenBy(turn => turn.Id).Take(limit).Select(turn => turn.Id)
                .ToArrayAsync(cancellationToken));
        if (ids.Count == 0) return [];
        return await content.Where(turn => ids.Contains(turn.Id))
            .OrderBy(turn => turn.ContentExpiresAtUtc).ThenBy(turn => turn.Id).Take(limit).ToListAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<AiAssistantRequestReceipt>> ListExpiredReceiptsAsync(DateTime nowUtc, int limit, CancellationToken cancellationToken) =>
        await dbContext.Set<AiAssistantRequestReceipt>().Where(receipt => receipt.ExpiresAtUtc <= nowUtc)
            .OrderBy(receipt => receipt.ExpiresAtUtc).ThenBy(receipt => receipt.Id).Take(limit).ToListAsync(cancellationToken);
    // I3: IgnoreQueryFilters, which the deleted account needs, applies to the whole query, so the state leaves out the retired ones.
    public async Task<IReadOnlyList<AiConversation>> ListDeletedOwnerConversationsAsync(int limit, CancellationToken cancellationToken) =>
        await dbContext.Set<AiConversation>().Where(conversation => conversation.State == AiConversationState.Active
                && dbContext.Users.IgnoreQueryFilters().Any(user => user.Id == conversation.OwnerUserId && user.IsDeleted))
            .OrderBy(conversation => conversation.Id).Take(limit).ToListAsync(cancellationToken);
    // I20: the removed ones by their filtered index, in its order, and the active ones apart; the repository joins their Ids and the final
    // query sorts them in the same order and cuts the limit.
    public async Task<IReadOnlyList<AiConversation>> ListRemovableConversationsAsync(DateTime nowUtc, int limit, CancellationToken cancellationToken)
    {
        var ids = new HashSet<Guid>();
        foreach (var state in new[] { AiConversationState.Removed, AiConversationState.Active })
            ids.UnionWith(await Removable(nowUtc).Where(conversation => conversation.State == state)
                .OrderBy(conversation => conversation.RemovalRequestedAtUtc).ThenBy(conversation => conversation.Id).Take(limit)
                .Select(conversation => conversation.Id).ToArrayAsync(cancellationToken));
        if (ids.Count == 0) return [];
        return await dbContext.Set<AiConversation>().IgnoreQueryFilters().Where(conversation => ids.Contains(conversation.Id))
            .OrderBy(conversation => conversation.RemovalRequestedAtUtc).ThenBy(conversation => conversation.Id).Take(limit).ToListAsync(cancellationToken);
    }
    // I3: Home goes like the other channels, even if nobody removed it; a removed Home conversation does not wait for its receipts. I20: the
    // live receipts by conversation have their index; content that has not expired and active work are asked apart.
    private IQueryable<AiConversation> Removable(DateTime nowUtc) =>
        dbContext.Set<AiConversation>().IgnoreQueryFilters().Where(conversation =>
            ((conversation.ChannelId == "home" && conversation.State == AiConversationState.Removed)
                || !dbContext.Set<AiAssistantRequestReceipt>().Any(receipt => receipt.ConversationId == conversation.Id && receipt.ExpiresAtUtc > nowUtc))
            && !dbContext.Set<AiAssistantTurn>().IgnoreQueryFilters().Any(turn => turn.ConversationId == conversation.Id && turn.ContentExpiresAtUtc > nowUtc)
            && !dbContext.Set<AiAssistantTurn>().IgnoreQueryFilters().Any(turn => turn.ConversationId == conversation.Id
                && (turn.State == AiAssistantTurnState.Pending || turn.State == AiAssistantTurnState.Running)));
    public async Task<IReadOnlyList<AiAssistantToolExecution>> ListToolsAsync(Guid turnId, CancellationToken cancellationToken) =>
        await dbContext.Set<AiAssistantToolExecution>().Where(tool => tool.TurnId == turnId)
            .OrderBy(tool => tool.Ordinal).ToListAsync(cancellationToken);

    public void AddConversation(AiConversation conversation) { dbContext.RequireTransaction(); dbContext.Set<AiConversation>().Add(conversation); }
    // I10: the turn keeps the trace of whoever submits it (the request in Home, the round in a channel), which the coordinator continues.
    public void AddTurn(AiAssistantTurn turn)
    {
        dbContext.RequireTransaction();
        if (AiAssistantTracing.Current() is { } trace) turn.RecordTrace(trace);
        dbContext.Set<AiAssistantTurn>().Add(turn);
    }
    public void AddReceipt(AiAssistantRequestReceipt receipt) { dbContext.RequireTransaction(); dbContext.Set<AiAssistantRequestReceipt>().Add(receipt); }
    public void AddTool(AiAssistantToolExecution execution) { dbContext.RequireTransaction(); dbContext.Set<AiAssistantToolExecution>().Add(execution); }
    public void RemoveTool(AiAssistantToolExecution execution) { dbContext.RequireTransaction(); dbContext.Set<AiAssistantToolExecution>().Remove(execution); }
    public void RemoveReceipt(AiAssistantRequestReceipt receipt) { dbContext.RequireTransaction(); dbContext.Set<AiAssistantRequestReceipt>().Remove(receipt); }
    public void RemoveConversation(AiConversation conversation) { dbContext.RequireTransaction(); dbContext.Set<AiConversation>().Remove(conversation); }
}
