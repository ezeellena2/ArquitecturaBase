using ArquitecturaBase.Application.Modules.ArtificialIntelligence.Configuration;
using ArquitecturaBase.Application.Modules.ArtificialIntelligence.Interfaces.Integrations;
using ArquitecturaBase.Application.Modules.ArtificialIntelligence.Interfaces.Persistence;
using ArquitecturaBase.Application.Modules.ArtificialIntelligence.Models;
using ArquitecturaBase.Domain.Modules.ArtificialIntelligence;
using ArquitecturaBase.Domain.Results;

namespace ArquitecturaBase.Application.Modules.ArtificialIntelligence.Services;

internal sealed class AiAssistantSubmissionPolicy(IAiAssistantRepository repository, IAiConnectionRepository connections, IAiConnectionReader connectionReader, AiAssistantAccessGuard guard, IAiConversationProtector protector, IAiAssistantRequestFingerprint fingerprint, AiAssistantLimits limits, TimeProvider timeProvider)
{
    public async Task<Result<AiAssistantTurnAcceptedResponse>> AcceptAsync(Guid userId, SubmitAiAssistantTurnRequest request, CancellationToken ct)
    {
        await connections.LockAsync(ct);
        await repository.LockBudgetAsync(ct);
        await repository.LockActorAsync(userId, ct);
        var actor = await guard.AuthorizeAsync(userId, ct);
        if (actor.IsFailure) return actor.Error;
        var receipt = await repository.GetReceiptAsync(userId, request.RequestId, ct);
        var now = timeProvider.GetUtcNow().UtcDateTime;
        if (receipt is not null)
        {
            if (!fingerprint.Matches(userId, request, receipt.KeyId, receipt.Fingerprint)) return AiAssistantErrors.RequestReused;
            var existing = await repository.GetTurnAsync(receipt.TurnId, ct);
            var previousConversation = await repository.GetConversationAsync(userId, receipt.ConversationId, ct);
            if (existing is null || previousConversation is null) return AiAssistantErrors.NotFound;
            return Accepted(existing, previousConversation.Revision, now);
        }
        AiConversation? conversation = null;
        if (request.ConversationId is { } conversationId)
        {
            await repository.LockConversationAsync(conversationId, ct);
            conversation = await repository.GetConversationAsync(userId, conversationId, ct);
            if (conversation is null) return AiAssistantErrors.NotFound;
            if (conversation.Revision != request.ExpectedRevision) return AiAssistantErrors.RevisionConflict;
            if (!await guard.CanReadAsync(userId, conversation.RequiredPermissions, ct)) return AiAssistantErrors.ContentBlocked;
        }
        var connection = await connectionReader.FindAsync(ct);
        if (connection.Revision != request.ExpectedConnectionRevision) return AiAssistantErrors.ConnectionChanged;
        if (guard.Availability(connection) != "available") return AiAssistantErrors.Unavailable;
        if (await repository.GetActiveForActorAsync(userId, ct) is not null) return AiAssistantErrors.ActiveTurn;
        if (conversation is not null && await repository.GetActiveForConversationAsync(conversation.Id, ct) is not null) return AiAssistantErrors.ActiveTurn;
        var budget = await repository.CountBudgetAsync(userId, DateOnly.FromDateTime(now), ct);
        if (budget.ActorAccepted >= limits.ActorDailyQuota || budget.InstallationAccepted >= limits.InstallationDailyQuota)
            return AiAssistantErrors.QuotaExceeded((int)(now.Date.AddDays(1) - now).TotalSeconds);
        if (budget.Pending >= limits.PendingCapacity) return AiAssistantErrors.CapacityLimited(limits.PollingAfterSeconds);
        if (conversation is null)
        {
            conversation = AiConversation.Create(userId, now);
            repository.AddConversation(conversation);
        }
        var ordinal = await repository.CountTurnsAsync(conversation.Id, ct) + 1;
        var turn = AiAssistantTurn.Create(userId, conversation.Id, request.RequestId, ordinal, "", request.Culture,
            actor.Value.TimeZoneId, connection.Provider, connection.Model, connection.Revision, now, now.AddDays(limits.RetentionDays));
        turn.ProtectQuestion(protector.Protect(userId, conversation.Id, turn.Id, "question", turn.PayloadRevision, request.NormalizedQuestion));
        conversation.Touch(now);
        repository.AddTurn(turn);
        repository.AddReceipt(AiAssistantRequestReceipt.Create(userId, request.RequestId, fingerprint.Create(userId, request, fingerprint.ActiveKeyId),
            fingerprint.ActiveKeyId, conversation.Id, turn.Id, now, now.AddDays(limits.RetentionDays)));
        return Accepted(turn, conversation.Revision, now);
    }

    private AiAssistantTurnAcceptedResponse Accepted(AiAssistantTurn turn, long conversationRevision, DateTime nowUtc) =>
        new(turn.ConversationId, turn.RequestId, conversationRevision, turn.Revision, AiAssistantResponsePolicy.State(turn.State),
            turn.Stage, turn.SubmittedAtUtc, nowUtc, limits.PollingAfterSeconds);
}
