using ArquitecturaBase.Application.Common.Logging;
using Microsoft.Extensions.Logging;
using ArquitecturaBase.Application.Interfaces.Persistence;
using ArquitecturaBase.Application.Modules.ArtificialIntelligence.Configuration;
using ArquitecturaBase.Application.Modules.ArtificialIntelligence.Interfaces.Persistence;
using ArquitecturaBase.Application.Modules.ArtificialIntelligence.Interfaces.Services;
using ArquitecturaBase.Domain.Results;

namespace ArquitecturaBase.Application.Modules.ArtificialIntelligence.Services;

internal sealed class AiAssistantRetentionService(IAiAssistantRepository repository, IUnitOfWork unitOfWork,
    AiAssistantLimits limits, TimeProvider timeProvider, ILogger<AiAssistantRetentionService> logger,
    IUserReader users) : IAiAssistantRetentionService
{
    public Task<Result<int>> ReconcileExpiredBatchAsync(CancellationToken cancellationToken) =>
        OperationLog.RunAsync<Result<int>>(logger, "ReconcileExpiredBatchAsync", () =>
        unitOfWork.ExecuteInTransactionAsync(async ct =>
        {
            await repository.LockBudgetAsync(ct);
            var now = timeProvider.GetUtcNow().UtcDateTime;
            var turns = await repository.ListExpiredAsync(now, limits.PendingSeconds, limits.LeaseGraceSeconds, limits.MaintenanceBatchSize, ct);
            foreach (var owner in turns.Select(t => t.OwnerUserId).Distinct().Order()) await repository.LockActorAsync(owner, ct);
            foreach (var conversation in turns.Select(t => t.ConversationId).Distinct().Order()) await repository.LockConversationAsync(conversation, ct);
            foreach (var turn in turns) turn.Reconcile(now, limits.PendingSeconds, limits.LeaseGraceSeconds);
            return Result.Success(turns.Count);
        }, CommitPolicy.OnSuccess, cancellationToken));

    public Task<Result<int>> PurgeBatchAsync(CancellationToken cancellationToken) =>
        OperationLog.RunAsync<Result<int>>(logger, "PurgeBatchAsync", () =>
        unitOfWork.ExecuteInTransactionAsync(async ct =>
        {
            await repository.LockBudgetAsync(ct);
            var now = timeProvider.GetUtcNow().UtcDateTime;
            var turns = await repository.ListPurgeableAsync(now, limits.MaintenanceBatchSize, ct);
            var conversations = await repository.ListRemovableConversationsAsync(now, limits.MaintenanceBatchSize - turns.Count, ct);
            foreach (var owner in turns.Select(t => t.OwnerUserId).Concat(conversations.Select(c => c.OwnerUserId)).Distinct().Order())
                await repository.LockActorAsync(owner, ct);
            foreach (var conversation in turns.Select(t => t.ConversationId).Concat(conversations.Select(c => c.Id)).Distinct().Order())
                await repository.LockConversationAsync(conversation, ct);
            var deletedOwners = new HashSet<Guid>();
            foreach (var owner in turns.Select(t => t.OwnerUserId).Distinct())
                if (await users.FindByIdAsync(owner, ct) is null) deletedOwners.Add(owner);
            foreach (var turn in turns)
            {
                if (deletedOwners.Contains(turn.OwnerUserId))
                {
                    turn.RequestCancellation(now);
                    var conversation = await repository.GetConversationAsync(turn.OwnerUserId, turn.ConversationId, ct);
                    conversation?.Remove(now);
                }
                turn.PurgeContent();
                foreach (var tool in await repository.ListToolsAsync(turn.Id, ct)) repository.RemoveTool(tool);
            }
            foreach (var conversation in conversations) repository.RemoveConversation(conversation);
            var receipts = await repository.ListExpiredReceiptsAsync(now, limits.MaintenanceBatchSize - turns.Count - conversations.Count, ct);
            foreach (var receipt in receipts) repository.RemoveReceipt(receipt);
            return Result.Success(turns.Count + conversations.Count + receipts.Count);
        }, CommitPolicy.OnSuccess, cancellationToken));
}
