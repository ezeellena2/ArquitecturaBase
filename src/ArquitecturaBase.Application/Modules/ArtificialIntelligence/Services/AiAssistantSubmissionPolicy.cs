using System.Text.Json;
using ArquitecturaBase.Application.Models.Assistant;
using ArquitecturaBase.Application.Modules.ArtificialIntelligence.Configuration;
using ArquitecturaBase.Application.Modules.ArtificialIntelligence.Interfaces.Integrations;
using ArquitecturaBase.Application.Modules.ArtificialIntelligence.Interfaces.Persistence;
using ArquitecturaBase.Application.Modules.ArtificialIntelligence.Models;
using ArquitecturaBase.Domain.Modules.ArtificialIntelligence;
using ArquitecturaBase.Domain.Results;

namespace ArquitecturaBase.Application.Modules.ArtificialIntelligence.Services;

internal sealed class AiAssistantSubmissionPolicy(IAiAssistantRepository repository, IAiConnectionRepository connections,
    IAiConnectionReader connectionReader, AiAssistantAccessGuard guard, IAiConversationProtector protector,
    IAiAssistantRequestFingerprint fingerprint, AiAssistantLimits limits, TimeProvider timeProvider)
{
    public Task<Result<AiAssistantTurnAcceptedResponse>> AcceptAsync(Guid userId, SubmitAiAssistantTurnRequest request, CancellationToken ct) =>
        AcceptCoreAsync(userId, request, null, ct);

    public async Task<Result<AssistantOperationAcceptedResponse>> AcceptAsync(AssistantOperationRequest request, CancellationToken ct)
    {
        var result = await AcceptCoreAsync(request.Context.ActorUserId, null, request, ct);
        return result.IsFailure ? result.Error : new AssistantOperationAcceptedResponse(result.Value.RequestId, result.Value.State);
    }

    public async Task<Result> CancelAsync(AssistantChannelContext context, Guid requestId, CancellationToken ct)
    {
        if (context.ChannelId == "home" || requestId == Guid.Empty) return AiAssistantErrors.NotFound;
        await repository.LockBudgetAsync(ct);
        await repository.LockActorAsync(context.ActorUserId, ct);
        var receipt = await repository.GetReceiptAsync(context.ActorUserId, requestId, ct);
        if (receipt is null) return Result.Success();
        await repository.LockConversationAsync(receipt.ConversationId, ct);
        var conversation = await repository.GetConversationAsync(context.ActorUserId, receipt.ConversationId, ct);
        var turn = await repository.GetTurnAsync(receipt.TurnId, ct);
        if (turn is null || conversation?.ChannelId != context.ChannelId || turn.ChannelId != context.ChannelId)
            return AiAssistantErrors.NotFound;
        var captured = AiAssistantChannelPolicy.Read(turn.ExecutionProfileJson);
        if (captured.IsFailure || !AiAssistantChannelPolicy.Matches(captured.Value, turn.OwnerUserId, context.ChannelId, turn.Culture, turn.TimeZoneId) ||
            !AiAssistantChannelPolicy.SameScope(context, captured.Value.ChannelContext)) return AiAssistantErrors.NotFound;
        turn.RequestCancellation(timeProvider.GetUtcNow().UtcDateTime);
        return Result.Success();
    }

    public Error? ValidateOperation(AssistantOperationRequest request)
    {
        if (request.RequestId == Guid.Empty || !Enum.IsDefined(request.Context.InteractionMode) || request.Context.ChannelId == "home" ||
            request.OccurredAtUtc.Kind != DateTimeKind.Utc || request.ContentExpiresAtUtc.Kind != DateTimeKind.Utc ||
            request.ContentExpiresAtUtc <= request.OccurredAtUtc) return AiAssistantErrors.Invalid;
        if (request.Audio is { } audio)
        {
            if (request.Text.Length != 0 || request.Context.AllowedCapabilities.Count != 0 || request.Context.AllowedTerminals.Count != 0 ||
                audio.ExpiresAtUtc <= request.OccurredAtUtc) return AiAssistantErrors.Invalid;
            return AiAssistantAudioPolicy.Validate(audio);
        }
        if (string.IsNullOrWhiteSpace(request.Text) || request.Text.Length > limits.QuestionCharacters ||
            request.Context.AllowedTerminals.Count != 1 || request.Context.AllowedTerminals[0] != new AssistantTerminalReference("assistant_proposal", 1))
            return AiAssistantErrors.Invalid;
        return null;
    }

    private async Task<Result<AiAssistantTurnAcceptedResponse>> AcceptCoreAsync(Guid userId, SubmitAiAssistantTurnRequest? home,
        AssistantOperationRequest? operation, CancellationToken ct)
    {
        var requestId = home?.RequestId ?? operation!.RequestId;
        var channelId = home is not null ? "home" : operation!.Context.ChannelId;
        await connections.LockAsync(ct);
        await repository.LockBudgetAsync(ct);
        await repository.LockActorAsync(userId, ct);
        var actor = home is not null ? await guard.AuthorizeAsync(userId, ct) : await guard.AuthorizeAsync(operation!.Context, ct);
        if (actor.IsFailure) return actor.Error;
        var receipt = await repository.GetReceiptAsync(userId, requestId, ct);
        var now = timeProvider.GetUtcNow().UtcDateTime;
        if (receipt is not null)
        {
            var matches = home is not null ? fingerprint.Matches(userId, home, receipt.KeyId, receipt.Fingerprint)
                : fingerprint.Matches(operation!, receipt.KeyId, receipt.Fingerprint);
            if (!matches) return AiAssistantErrors.RequestReused;
            var existing = await repository.GetTurnAsync(receipt.TurnId, ct);
            var previousConversation = await repository.GetConversationAsync(userId, receipt.ConversationId, ct);
            if (existing is null || previousConversation is null || previousConversation.ChannelId != channelId) return AiAssistantErrors.NotFound;
            return Accepted(existing, previousConversation.Revision, now);
        }
        AiConversation? conversation = null;
        if (home?.ConversationId is { } conversationId)
        {
            await repository.LockConversationAsync(conversationId, ct);
            conversation = await repository.GetConversationAsync(userId, conversationId, ct);
            if (conversation is null || conversation.ChannelId != "home") return AiAssistantErrors.NotFound;
            if (conversation.Revision != home.ExpectedRevision) return AiAssistantErrors.RevisionConflict;
            if (!await guard.CanReadAsync(userId, conversation.RequiredPermissions, ct)) return AiAssistantErrors.ContentBlocked;
        }
        var connection = await connectionReader.FindAsync(ct);
        if (home is not null && connection.Revision != home.ExpectedConnectionRevision) return AiAssistantErrors.ConnectionChanged;
        if (operation?.Audio is null && guard.Availability(connection) != "available") return AiAssistantErrors.Unavailable;
        var culture = home?.Culture ?? operation!.Context.Culture;
        var timeZoneId = home is not null ? actor.Value.TimeZoneId : operation!.Context.TimeZoneId;
        var profile = home is not null ? await guard.CaptureHomeProfileAsync(connection, userId, culture, timeZoneId, ct)
            : operation!.Audio is { } audioInput ? guard.CaptureAudioProfile(connection, operation.Context, audioInput, operation.OccurredAtUtc)
            : await guard.CaptureOperationProfileAsync(connection, operation.Context, operation.OccurredAtUtc, ct);
        if (profile.IsFailure) return profile.Error;
        var expiresAtUtc = now.AddDays(limits.RetentionDays);
        if (operation is not null && operation.ContentExpiresAtUtc < expiresAtUtc) expiresAtUtc = operation.ContentExpiresAtUtc;
        if (operation?.Audio is { } source && source.ExpiresAtUtc < expiresAtUtc) expiresAtUtc = source.ExpiresAtUtc;
        if (expiresAtUtc <= now) return AiAssistantErrors.Expired;
        if (channelId != "home" && profile.Value.Purpose != AiAssistantPurpose.NormalizeAudio
            && await repository.GetActiveForActorChannelAsync(userId, channelId, operation?.Context.ChannelConversationId, ct) is not null) return AiAssistantErrors.ActiveTurn;
        if (conversation is not null && await repository.GetActiveForConversationAsync(conversation.Id, ct) is not null) return AiAssistantErrors.ActiveTurn;
        var budget = await repository.CountBudgetAsync(userId, DateOnly.FromDateTime(now), channelId, profile.Value.Purpose == AiAssistantPurpose.NormalizeAudio, ct);
        if (budget.ActorAccepted >= limits.ActorDailyQuota || budget.InstallationAccepted >= limits.InstallationDailyQuota)
            return AiAssistantErrors.QuotaExceeded((int)(now.Date.AddDays(1) - now).TotalSeconds);
        if (budget.Pending >= limits.PendingCapacity) return AiAssistantErrors.CapacityLimited(limits.PollingAfterSeconds);
        if (conversation is null)
        {
            conversation = AiConversation.Create(userId, now, channelId);
            repository.AddConversation(conversation);
        }
        var ordinal = await repository.CountTurnsAsync(conversation.Id, ct) + 1;
        var turn = AiAssistantTurn.Create(userId, conversation.Id, requestId, ordinal, "", culture,
            timeZoneId, connection.Provider, guard.Model(profile.Value, connection)!, connection.Revision, now, expiresAtUtc, channelId, operation?.Context.ChannelConversationId);
        var sealedProfile = turn.SealExecutionProfile(JsonSerializer.Serialize(profile.Value, AiAssistantResponsePolicy.JsonOptions));
        if (sealedProfile.IsFailure) return sealedProfile.Error;
        var question = operation?.Audio is { } audio ? JsonSerializer.Serialize(audio, AiAssistantResponsePolicy.JsonOptions)
            : home?.NormalizedQuestion ?? operation!.Text.Trim().Replace("\r\n", "\n", StringComparison.Ordinal);
        turn.ProtectQuestion(protector.Protect(userId, conversation.Id, turn.Id, "question", turn.PayloadRevision, question));
        conversation.Touch(now);
        repository.AddTurn(turn);
        var hash = home is not null ? fingerprint.Create(userId, home, fingerprint.ActiveKeyId) : fingerprint.Create(operation!, fingerprint.ActiveKeyId);
        repository.AddReceipt(AiAssistantRequestReceipt.Create(userId, requestId, hash,
            fingerprint.ActiveKeyId, conversation.Id, turn.Id, now, now.AddDays(limits.RetentionDays), channelId, profile.Value.Purpose == AiAssistantPurpose.NormalizeAudio));
        return Accepted(turn, conversation.Revision, now);
    }

    private AiAssistantTurnAcceptedResponse Accepted(AiAssistantTurn turn, long conversationRevision, DateTime nowUtc) =>
        new(turn.ConversationId, turn.RequestId, conversationRevision, turn.Revision, AiAssistantResponsePolicy.State(turn.State),
            turn.Stage, turn.SubmittedAtUtc, nowUtc, limits.PollingAfterSeconds);
}
