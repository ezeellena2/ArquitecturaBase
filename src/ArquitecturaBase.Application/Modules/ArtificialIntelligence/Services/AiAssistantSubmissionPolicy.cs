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
        if (request.PreviousRequestId is { } previous && (previous == Guid.Empty || previous == request.RequestId || request.Audio is not null ||
            request.Document is not null)) return AiAssistantErrors.Invalid;
        // A document is read alone, never with text, an audio, a spreadsheet or tools, and its copy lasts beyond its message.
        if (request.Document is { } document)
        {
            if (request.Text.Length != 0 || request.Audio is not null || request.Attachment is not null || request.Context.AllowedCapabilities.Count != 0 ||
                request.Context.AllowedTerminals.Count != 0 || document.ExpiresAtUtc <= request.OccurredAtUtc) return AiAssistantErrors.Invalid;
            return AiAssistantDocumentPolicy.Validate(document);
        }
        // A spreadsheet goes with an interpretation, never with an audio to normalize, and lasts beyond its message.
        if (request.Attachment is { } attachment && (request.Audio is not null || attachment.ExpiresAtUtc <= request.OccurredAtUtc ||
            AiAssistantAttachmentPolicy.Validate(attachment) is not null)) return AiAssistantErrors.Invalid;
        if (request.Audio is { } audio)
        {
            if (request.Text.Length != 0 || request.Context.AllowedCapabilities.Count != 0 || request.Context.AllowedTerminals.Count != 0 ||
                audio.ExpiresAtUtc <= request.OccurredAtUtc) return AiAssistantErrors.Invalid;
            return AiAssistantAudioPolicy.Validate(audio);
        }
        // An interpretation is written, except a spreadsheet that answers its conversation («Cambiar algo» or a question): its message
        // carries the sheet.
        if (string.IsNullOrWhiteSpace(request.Text) && (request.Attachment is null || request.PreviousRequestId is null) ||
            request.Text.Length > limits.QuestionCharacters ||
            request.Context.AllowedTerminals.Count != 1 || request.Context.AllowedTerminals[0] != new AssistantTerminalReference("assistant_proposal", 1))
            return AiAssistantErrors.Invalid;
        return null;
    }

    private async Task<Result<AiAssistantTurnAcceptedResponse>> AcceptCoreAsync(Guid userId, SubmitAiAssistantTurnRequest? home,
        AssistantOperationRequest? operation, CancellationToken ct)
    {
        var requestId = home?.RequestId ?? operation!.RequestId;
        var channelId = home is not null ? "home" : operation!.Context.ChannelId;
        if (home?.Attachment is { } homeAttachment && AiAssistantAttachmentPolicy.Validate(homeAttachment) is { } invalid) return invalid;
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
        else if (operation?.PreviousRequestId is { } previousRequestId)
        {
            var continued = await ContinueAsync(operation.Context, previousRequestId, now, ct);
            if (continued.IsFailure) return continued.Error;
            conversation = continued.Value;
        }
        string? transcript = null;
        if (home?.AudioRequestId is { } audioRequestId)
        {
            var heard = await TranscriptAsync(userId, audioRequestId, now, ct);
            if (heard.IsFailure) return heard.Error;
            transcript = heard.Value;
        }
        var connection = await connectionReader.FindAsync(ct);
        if (home is not null && connection.Revision != home.ExpectedConnectionRevision) return AiAssistantErrors.ConnectionChanged;
        var normalizes = operation?.Audio is not null;
        var reads = operation?.Document is not null;
        // The technical purposes take the model of their own profile: the text model's qualification does not decide them.
        if (!normalizes && !reads && guard.Availability(connection) != "available") return AiAssistantErrors.Unavailable;
        var culture = home?.Culture ?? operation!.Context.Culture;
        var timeZoneId = home is not null ? actor.Value.TimeZoneId : operation!.Context.TimeZoneId;
        // The turn keeps its content no longer than its sources: the channel's message, the audio or document and its own spreadsheet.
        var attachment = home?.Attachment ?? operation?.Attachment;
        var expiresAtUtc = now.AddDays(limits.RetentionDays);
        if (operation is not null && operation.ContentExpiresAtUtc < expiresAtUtc) expiresAtUtc = operation.ContentExpiresAtUtc;
        if (operation?.Audio is { } source && source.ExpiresAtUtc < expiresAtUtc) expiresAtUtc = source.ExpiresAtUtc;
        if (operation?.Document is { } copy && copy.ExpiresAtUtc < expiresAtUtc) expiresAtUtc = copy.ExpiresAtUtc;
        if (attachment is not null && attachment.ExpiresAtUtc < expiresAtUtc) expiresAtUtc = attachment.ExpiresAtUtc;
        if (expiresAtUtc <= now) return AiAssistantErrors.Expired;
        // Only an interpretation reserves its channel conversation; an audio or a document never takes or waits for that slot.
        if (channelId != "home" && !normalizes && !reads
            && await repository.GetActiveForActorChannelAsync(userId, channelId, operation?.Context.ChannelConversationId, ct) is not null) return AiAssistantErrors.ActiveTurn;
        if (conversation is not null && await repository.GetActiveForConversationAsync(conversation.Id, ct) is not null) return AiAssistantErrors.ActiveTurn;
        // Readings count per person across every channel and per installation, apart from the quotas of interpretations and audio.
        var budget = await repository.CountBudgetAsync(userId, DateOnly.FromDateTime(now), channelId, normalizes, reads, ct);
        var (actorQuota, installationQuota) = reads ? (limits.DocumentActorDailyQuota, limits.DocumentInstallationDailyQuota)
            : (limits.ActorDailyQuota, limits.InstallationDailyQuota);
        if (budget.ActorAccepted >= actorQuota || budget.InstallationAccepted >= installationQuota)
            return AiAssistantErrors.QuotaExceeded((int)(now.Date.AddDays(1) - now).TotalSeconds);
        if (budget.Pending >= limits.PendingCapacity) return AiAssistantErrors.CapacityLimited(limits.PollingAfterSeconds);
        // Only after the cheap refusals, under the lock of its conversation, a turn without a spreadsheet of its own reads the
        // conversation's; one that expires on its own bounds the turn.
        if (attachment is null && conversation is not null)
        {
            var inherited = await InheritedAttachmentAsync(userId, conversation.Id, now, expiresAtUtc, ct);
            if (inherited.IsFailure) return inherited.Error;
            attachment = inherited.Value;
            if (attachment is not null && attachment.ExpiresAtUtc < expiresAtUtc) expiresAtUtc = attachment.ExpiresAtUtc;
        }
        var profile = home is not null ? await guard.CaptureHomeProfileAsync(connection, userId, culture, timeZoneId, attachment is not null, ct)
            : operation!.Audio is { } audioInput ? guard.CaptureAudioProfile(connection, operation.Context, audioInput, operation.OccurredAtUtc)
            : operation.Document is { } documentInput ? guard.CaptureDocumentProfile(connection, operation.Context, documentInput, operation.OccurredAtUtc)
            : await guard.CaptureOperationProfileAsync(connection, operation.Context, operation.OccurredAtUtc, attachment is not null, ct);
        if (profile.IsFailure) return profile.Error;
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
            : operation?.Document is { } document ? AiAssistantDocumentPolicy.Serialize(document)
            : home?.NormalizedQuestion ?? operation!.Text.Trim().Replace("\r\n", "\n", StringComparison.Ordinal);
        turn.ProtectQuestion(protector.Protect(userId, conversation.Id, turn.Id, "question", turn.PayloadRevision, question));
        if (transcript is not null)
            turn.AttachAudio(home!.AudioRequestId!.Value, protector.Protect(userId, conversation.Id, turn.Id, "transcript", turn.PayloadRevision, transcript));
        if (attachment is not null)
            turn.Attach(attachment.StorageKey, attachment.Sha256, attachment.Size, protector.Protect(userId, conversation.Id, turn.Id,
                AiAssistantAttachmentPolicy.Purpose, turn.PayloadRevision, AiAssistantAttachmentPolicy.Serialize(attachment)));
        conversation.Touch(now);
        repository.AddTurn(turn);
        var hash = home is not null ? fingerprint.Create(userId, home, fingerprint.ActiveKeyId) : fingerprint.Create(operation!, fingerprint.ActiveKeyId);
        repository.AddReceipt(AiAssistantRequestReceipt.Create(userId, requestId, hash, fingerprint.ActiveKeyId, conversation.Id, turn.Id, now,
            now.AddDays(limits.RetentionDays), channelId, profile.Value.Purpose == AiAssistantPurpose.NormalizeAudio, profile.Value.Purpose == AiAssistantPurpose.ReadDocument));
        return Accepted(turn, conversation.Revision, now);
    }

    // A channel message that continues its previous operation joins that engine conversation, like Home: a completed interpretation of
    // the same people and channel conversation, while its content lasts and its reserve is readable. Later turns may have come after it
    // (one the channel replaced, a question): the channel decides which proposal is current, and the new turn goes at the end once no
    // operation of the conversation is active.
    private async Task<Result<AiConversation>> ContinueAsync(AssistantChannelContext context, Guid previousRequestId, DateTime nowUtc, CancellationToken ct)
    {
        var receipt = await repository.GetReceiptAsync(context.ActorUserId, previousRequestId, ct);
        if (receipt is null) return AiAssistantErrors.NotFound;
        await repository.LockConversationAsync(receipt.ConversationId, ct);
        var conversation = await repository.GetConversationAsync(context.ActorUserId, receipt.ConversationId, ct);
        var previous = await repository.GetTurnAsync(receipt.TurnId, ct);
        if (conversation is null || previous is null || conversation.ChannelId != context.ChannelId || previous.State != AiAssistantTurnState.Completed)
            return AiAssistantErrors.NotFound;
        var captured = AiAssistantChannelPolicy.Read(previous.ExecutionProfileJson);
        if (captured.IsFailure || captured.Value.Purpose != AiAssistantPurpose.Interpret ||
            !AiAssistantChannelPolicy.SameConversation(context, captured.Value.ChannelContext)) return AiAssistantErrors.NotFound;
        if (previous.ContentExpiresAtUtc <= nowUtc || previous.ProtectedQuestion is null) return AiAssistantErrors.Expired;
        if (!await guard.CanReadAsync(context.ActorUserId, conversation.RequiredPermissions, ct)) return AiAssistantErrors.ContentBlocked;
        return conversation;
    }

    // A turn without a spreadsheet of its own keeps its conversation's: the one of the latest turn that has a spreadsheet, while that
    // turn keeps its content; a replaced one never comes back, and the new turn seals it again. A Home copy stays while a turn has it,
    // so it takes the new turn's expiry and inheriting never ends with the first turn; any other copy expires on its own.
    private async Task<Result<AssistantAttachmentInput?>> InheritedAttachmentAsync(Guid userId, Guid conversationId, DateTime nowUtc,
        DateTime contentExpiresAtUtc, CancellationToken ct)
    {
        var latest = await repository.FindLatestAttachmentAsync(conversationId, ct);
        if (latest?.ProtectedAttachment is not { } protectedAttachment || latest.ContentExpiresAtUtc <= nowUtc)
            return Result.Success<AssistantAttachmentInput?>(null);
        var attachment = AiAssistantAttachmentPolicy.Read(protector.Unprotect(userId, conversationId, latest.TurnId, AiAssistantAttachmentPolicy.Purpose,
            latest.PayloadRevision, protectedAttachment));
        if (attachment is null) return AiAssistantErrors.ContentUnavailable;
        if (AiAssistantHomeAttachmentPolicy.TryParseKey(attachment.StorageKey, out _))
            return Result.Success<AssistantAttachmentInput?>(attachment with { ExpiresAtUtc = contentExpiresAtUtc });
        return Result.Success<AssistantAttachmentInput?>(attachment.ExpiresAtUtc > nowUtc ? attachment : null);
    }

    // The text of a dictated Home turn comes from its completed normalization of the same person, never from the browser (IA4).
    private async Task<Result<string>> TranscriptAsync(Guid userId, Guid audioRequestId, DateTime nowUtc, CancellationToken ct)
    {
        var receipt = await repository.GetReceiptAsync(userId, audioRequestId, ct);
        if (receipt is null) return AiAssistantErrors.NotFound;
        var conversation = await repository.GetConversationAsync(userId, receipt.ConversationId, ct);
        var normalization = await repository.GetTurnAsync(receipt.TurnId, ct);
        if (conversation?.ChannelId != AiAssistantChannelPolicy.HomeAudioChannelId || normalization is null || normalization.OwnerUserId != userId)
            return AiAssistantErrors.NotFound;
        if (normalization.State != AiAssistantTurnState.Completed) return AiAssistantErrors.ContentUnavailable;
        if (normalization.ContentExpiresAtUtc <= nowUtc || normalization.ProtectedAnswer is null) return AiAssistantErrors.Expired;
        var json = protector.Unprotect(userId, normalization.ConversationId, normalization.Id, "answer", normalization.PayloadRevision, normalization.ProtectedAnswer);
        return json is not null && AiAssistantAudioPolicy.ReadNormalized(json, AiAssistantAudioPolicy.MaximumTranscriptCharacters) is { } audio
            ? audio.Text : AiAssistantErrors.ContentUnavailable;
    }

    private AiAssistantTurnAcceptedResponse Accepted(AiAssistantTurn turn, long conversationRevision, DateTime nowUtc) =>
        new(turn.ConversationId, turn.RequestId, conversationRevision, turn.Revision, AiAssistantResponsePolicy.State(turn.State),
            turn.Stage, turn.SubmittedAtUtc, nowUtc, limits.PollingAfterSeconds);
}
