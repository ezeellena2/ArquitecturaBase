using ArquitecturaBase.Application.Common.Logging;
using ArquitecturaBase.Application.Common.Time;
using Microsoft.Extensions.Logging;
using ArquitecturaBase.Application.Interfaces.Persistence;
using ArquitecturaBase.Application.Modules.ArtificialIntelligence.Configuration;
using ArquitecturaBase.Application.Modules.ArtificialIntelligence.Interfaces.Persistence;
using ArquitecturaBase.Application.Modules.ArtificialIntelligence.Interfaces.Services;
using ArquitecturaBase.Application.Modules.ArtificialIntelligence.Models;
using ArquitecturaBase.Domain.Modules.ArtificialIntelligence;
using ArquitecturaBase.Domain.Results;

namespace ArquitecturaBase.Application.Modules.ArtificialIntelligence.Services;

internal sealed class AiAssistantRetentionService(IAiAssistantRepository repository, IUnitOfWork unitOfWork,
    AiAssistantLimits limits, TimeProvider timeProvider, ILogger<AiAssistantRetentionService> logger,
    IUserReader users, IAiConnectionReader connections, ISystemSettingsReader settings) : IAiAssistantRetentionService
{
    // I2: a Home recording or reading that no turn named lasts a day, like an orphan copy of the private storage.
    private const int UnnamedSourceHours = 24;


    // A poll (I10): it runs every second, so it does not go through OperationLog; the scheduler logs a failure in Warning.
    public Task<Result<int>> ReconcileExpiredBatchAsync(CancellationToken cancellationToken) =>
        unitOfWork.ExecuteInTransactionAsync(async ct =>
        {
            await repository.LockBudgetAsync(ct);
            var now = timeProvider.GetUtcNow().UtcDateTime;
            var turns = await repository.ListExpiredAsync(now, limits.PendingSeconds, limits.LeaseGraceSeconds, limits.MaintenanceBatchSize, ct);
            foreach (var owner in turns.Select(t => t.OwnerUserId).Distinct().Order()) await repository.LockActorAsync(owner, ct);
            foreach (var conversation in turns.Select(t => t.ConversationId).Distinct().Order()) await repository.LockConversationAsync(conversation, ct);
            foreach (var turn in turns) turn.Reconcile(now, limits.PendingSeconds, limits.LeaseGraceSeconds);
            return Result.Success(turns.Count);
        }, CommitPolicy.OnSuccess, cancellationToken);

    public Task<Result<int>> PurgeBatchAsync(CancellationToken cancellationToken) =>
        OperationLog.RunAsync<Result<int>>(logger, "PurgeBatchAsync", () =>
        unitOfWork.ExecuteInTransactionAsync(async ct =>
        {
            await repository.LockBudgetAsync(ct);
            var now = timeProvider.GetUtcNow().UtcDateTime;
            // RC1-A: the Home days, read on every run. Shortened below the ceiling, what Home saved before goes in this cleanup, with its
            // receipts so its conversation goes too; at the ceiling, the expiry each one was saved with already says so.
            var homeDays = (await connections.FindAsync(ct)).HomeRetentionDays(limits.RetentionDays);
            AiAssistantHomeRetention? home = homeDays < limits.RetentionDays ? new(now.AddDays(-homeDays), AiAssistantChannelPolicy.HomeChannels) : null;
            var turns = await repository.ListPurgeableAsync(now, new(now.AddHours(-UnnamedSourceHours), AiAssistantChannelPolicy.HomeAudioChannelId,
                AiAssistantChannelPolicy.HomeDocumentChannelId, AiAssistantHomeDocumentPolicy.Prefix, AiAssistantHomeDocumentPolicy.KeyExtensions),
                home, limits.MaintenanceBatchSize, ct);
            var retiring = await repository.ListDeletedOwnerConversationsAsync(limits.MaintenanceBatchSize - turns.Count, ct);
            var conversations = await repository.ListRemovableConversationsAsync(now, home, limits.MaintenanceBatchSize - turns.Count - retiring.Count, ct);
            foreach (var owner in turns.Select(t => t.OwnerUserId).Concat(retiring.Concat(conversations).Select(c => c.OwnerUserId)).Distinct().Order())
                await repository.LockActorAsync(owner, ct);
            foreach (var conversation in turns.Select(t => t.ConversationId).Concat(retiring.Concat(conversations).Select(c => c.Id)).Distinct().Order())
                await repository.LockConversationAsync(conversation, ct);
            // I3: a deleted account retires its conversations even once they have no content left. An active turn always keeps its
            // question, so the content purge below cancels it, in this batch or the next one.
            foreach (var conversation in retiring) conversation.Remove(now);
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
                // Past the Home days its expiry comes forward too, so reads see it expired and its conversation does not wait for it.
                if (home is not null && AiAssistantChannelPolicy.IsHome(turn.ChannelId) && turn.SubmittedAtUtc <= home.SubmittedBeforeUtc) turn.ExpireContent(now);
                else turn.PurgeContent();
                foreach (var tool in await repository.ListToolsAsync(turn.Id, ct)) repository.RemoveTool(tool);
            }
            var zone = BusinessDay.Zone((await settings.FindPresentationAsync(ct)).DefaultTimeZoneId);
            // PR12: their turns go with them, so what they sent is added first to its month, in this same unit and under the budget lock.
            if (conversations.Count > 0) await KeepUsageAsync([.. conversations.Select(c => c.Id)], zone, ct);
            foreach (var conversation in conversations) repository.RemoveConversation(conversation);
            // I1: what the business day in course accepted counts in its quotas until its midnight, in the zone of the settings.
            var today = BusinessDay.Of(now, zone);
            var receipts = await repository.ListExpiredReceiptsAsync(now, today.StartUtc, home,
                limits.MaintenanceBatchSize - turns.Count - retiring.Count - conversations.Count, ct);
            foreach (var receipt in receipts) repository.RemoveReceipt(receipt);
            return Result.Success(turns.Count + retiring.Count + conversations.Count + receipts.Count);
        }, CommitPolicy.OnSuccess, cancellationToken));

    private async Task KeepUsageAsync(IReadOnlyCollection<Guid> conversationIds, TimeZoneInfo zone, CancellationToken cancellationToken)
    {
        // I4: a provider that a newer version saved is read Unsupported and never written; its turns go without being counted here.
        foreach (var row in AiUsagePolicy.Summarize(await repository.ListTurnUsageAsync(conversationIds, cancellationToken), zone)
            .Where(row => AiProviders.IsSupported(row.Usage.Provider)))
        {
            var usage = await repository.GetUsageMonthAsync(row.Month, row.Usage.Provider, row.Usage.Model, cancellationToken);
            if (usage is null)
            {
                usage = AiUsageMonth.Create(row.Month, row.Usage.Provider, row.Usage.Model);
                repository.AddUsageMonth(usage);
            }
            usage.Add(row.Usage.Requests, row.Usage.UnknownUsageRequests, row.Usage.InputTokens, row.Usage.OutputTokens, row.Usage.AudioSeconds);
        }
    }
}
