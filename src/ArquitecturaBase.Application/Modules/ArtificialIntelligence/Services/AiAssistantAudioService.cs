using ArquitecturaBase.Application.Common.Logging;
using ArquitecturaBase.Application.Common.Validation;
using ArquitecturaBase.Application.Interfaces.Channels;
using ArquitecturaBase.Application.Interfaces.Integrations.Request;
using ArquitecturaBase.Application.Models.Assistant;
using ArquitecturaBase.Application.Modules.ArtificialIntelligence.Configuration;
using ArquitecturaBase.Application.Modules.ArtificialIntelligence.Interfaces.Services;
using ArquitecturaBase.Application.Modules.ArtificialIntelligence.Models;
using ArquitecturaBase.Application.Modules.ArtificialIntelligence.Resources;
using ArquitecturaBase.Application.Resources;
using ArquitecturaBase.Domain.Modules.ArtificialIntelligence;
using ArquitecturaBase.Domain.Results;
using Microsoft.Extensions.Logging;

namespace ArquitecturaBase.Application.Modules.ArtificialIntelligence.Services;

// IA4: a Home recording is a private copy normalized by the engine's own NormalizeAudio in a channel of its own; the turn then
// names it and the server takes its transcript. Get comes first: retrying the same request with the same recording recovers it and
// never keeps or submits again; another recording under it is a conflict, as with a document (I13).
internal sealed class AiAssistantAudioService(IAssistantOperations operations, AiAssistantHomeAudioPolicy audio, AiAssistantAccessGuard guard,
    AiAssistantSubmissionPolicy submissions, ICurrentUser current, AiAssistantLimits limits, TimeProvider clock, ILogger<AiAssistantAudioService> logger) : IAiAssistantAudioService
{
    public Task<Result<AiAssistantAudioResponse>> SubmitAsync(SubmitAiAssistantAudioRequest request, CancellationToken cancellationToken) =>
        OperationLog.RunAsync<Result<AiAssistantAudioResponse>>(logger, "SubmitAiAssistantAudio", async () =>
        {
            if (current.UserId is not { } userId) return AiAssistantErrors.SessionRequired;
            if (request.RequestId == Guid.Empty || request.Culture is not ("es" or "en")) return AiAssistantErrors.Invalid;
            if (request.Content.Length is 0 or > SubmitAiAssistantAudioRequest.MaximumBytes
                || AiAssistantAudioPolicy.Detect(request.Content) is not { } mediaType) return FieldErrors.On(AiAssistantErrors.Invalid, "file");
            var actor = await guard.AuthorizeAsync(userId, cancellationToken);
            if (actor.IsFailure) return actor.Error;
            var context = AiAssistantChannelPolicy.HomeAudio(userId, request.Culture, actor.Value.TimeZoneId);
            var existing = await operations.GetAsync(context, request.RequestId, cancellationToken);
            if (existing.IsSuccess) return Recovered(existing.Value, request, mediaType);
            if (existing.Error != AiAssistantErrors.NotFound) return existing.Error;
            if (audio.Check() is { } unavailable) return unavailable;
            // I14: after the storage, what the acceptance would refuse (the quota, the capacity, the transcription profile) refuses before the
            // copy is kept. Another upload of the same request may have taken the last place since the lookup: its normalization answers both.
            if (await submissions.PrecheckAudioAsync(context, mediaType, request.Content.Length, cancellationToken) is { } refused)
                return await RecoverAsync(context, request, mediaType, cancellationToken) ?? refused;
            var now = clock.GetUtcNow().UtcDateTime;
            var expiresAtUtc = now.AddDays(limits.RetentionDays);
            var stored = await audio.StoreAsync(request.RequestId, request.Content, mediaType, expiresAtUtc, cancellationToken);
            if (stored.IsFailure) return stored.Error;
            var accepted = await operations.SubmitAsync(new AssistantOperationRequest(request.RequestId, context, "", now, expiresAtUtc)
                { Audio = stored.Value }, cancellationToken);
            if (accepted.IsSuccess) return new AiAssistantAudioResponse(request.RequestId, accepted.Value.State, null, null, limits.PollingAfterSeconds);
            // Another upload of the same request got there first with this same copy, sealed at another instant: its normalization answers both.
            if (accepted.Error != AiAssistantErrors.RequestReused) return accepted.Error;
            return await RecoverAsync(context, request, mediaType, cancellationToken) ?? accepted.Error;
        });

    // The normalization another upload of the same request accepted meanwhile, compared with this recording; null when there is none.
    private async Task<Result<AiAssistantAudioResponse>?> RecoverAsync(AssistantChannelContext context, SubmitAiAssistantAudioRequest request,
        string mediaType, CancellationToken ct)
    {
        var found = await operations.GetAsync(context, request.RequestId, ct);
        return found.IsSuccess ? Recovered(found.Value, request, mediaType) : null;
    }

    public Task<Result<AiAssistantAudioResponse>> GetAsync(Guid requestId, string culture, CancellationToken cancellationToken) =>
        OperationLog.RunAsync<Result<AiAssistantAudioResponse>>(logger, "GetAiAssistantAudio", async () =>
        {
            if (current.UserId is not { } userId) return AiAssistantErrors.SessionRequired;
            var actor = await guard.AuthorizeAsync(userId, cancellationToken);
            if (actor.IsFailure) return actor.Error;
            var found = await operations.GetAsync(AiAssistantChannelPolicy.HomeAudio(userId, culture, actor.Value.TimeZoneId), requestId, cancellationToken);
            return found.IsFailure ? found.Error : Response(found.Value, culture);
        });

    public Task<Result<AiAssistantAudioFileResponse>> OpenAsync(Guid requestId, CancellationToken cancellationToken) =>
        OperationLog.RunAsync<Result<AiAssistantAudioFileResponse>>(logger, "OpenAiAssistantAudio", async () =>
        {
            if (current.UserId is not { } userId) return AiAssistantErrors.SessionRequired;
            var actor = await guard.AuthorizeAsync(userId, cancellationToken);
            return actor.IsFailure ? actor.Error : await audio.OpenAsync(userId, requestId, cancellationToken);
        });

    // A retry is the same recording as the normalization's, in any state; another one under the request is a conflict. A normalization
    // whose content is gone has no descriptor to compare and nothing left to give, so it answers as it is.
    private Result<AiAssistantAudioResponse> Recovered(AssistantOperationResponse operation, SubmitAiAssistantAudioRequest request, string mediaType) =>
        operation.Audio is { } heard && !AiAssistantHomeAudioPolicy.IsSameFile(heard, request.Content, mediaType)
            ? AiAssistantErrors.RequestReused : Response(operation, request.Culture);

    private AiAssistantAudioResponse Response(AssistantOperationResponse operation, string culture) => new(operation.RequestId, operation.State,
        operation.NormalizedAudio?.Text, operation.FailureCode is { } code
            ? new AiAssistantFailure(code, ErrorMessages.Find(code) ?? AssistantTexts.Get(code, culture), "newAttempt") : null, limits.PollingAfterSeconds);
}
