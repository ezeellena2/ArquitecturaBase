using ArquitecturaBase.Application.Common.Logging;
using ArquitecturaBase.Application.Common.Validation;
using ArquitecturaBase.Application.Interfaces.Channels;
using ArquitecturaBase.Application.Models.Assistant;
using ArquitecturaBase.Application.Models.StructuredData;
using ArquitecturaBase.Application.Modules.ArtificialIntelligence.Configuration;
using ArquitecturaBase.Application.Modules.ArtificialIntelligence.Interfaces.Services;
using ArquitecturaBase.Application.Modules.ArtificialIntelligence.Models;
using ArquitecturaBase.Application.Modules.ArtificialIntelligence.Resources;
using ArquitecturaBase.Application.Resources;
using ArquitecturaBase.Domain.Modules.ArtificialIntelligence;
using ArquitecturaBase.Domain.Results;
using Microsoft.Extensions.Logging;

namespace ArquitecturaBase.Application.Modules.ArtificialIntelligence.Services;

// Stage 3: a Home photo or PDF is a private copy read by the engine's own ReadDocument in a channel of its own, like a recording; the
// turn then names it and the server takes the reading. Get comes first: retrying the same request with the same file recovers it and
// never reads, keeps or submits again; another file under it is a conflict, as in file-turns.
internal sealed class AiAssistantDocumentService(IAssistantOperations operations, AiAssistantHomeDocumentPolicy documents, AiAssistantAccessGuard guard,
    AiAssistantLimits limits, TimeProvider clock, ILogger<AiAssistantDocumentService> logger) : IAiAssistantDocumentService
{
    public Task<Result<AiAssistantDocumentResponse>> SubmitAsync(SubmitAiAssistantDocumentRequest request, CancellationToken cancellationToken) =>
        OperationLog.RunAsync<Result<AiAssistantDocumentResponse>>(logger, "SubmitAiAssistantDocument", async () =>
        {
            ArgumentNullException.ThrowIfNull(request);
            if (guard.CurrentUserId is not { } userId) return AiAssistantErrors.SessionRequired;
            if (request.RequestId == Guid.Empty || request.Culture is not ("es" or "en")) return AiAssistantErrors.Invalid;
            // The browser checks the limit first: past it, or empty, it is a request error.
            if (request.Content.Length is 0 or > StructuredAttachmentLimits.MaximumBytes) return FieldErrors.On(AiAssistantErrors.Invalid, "file");
            var actor = await guard.AuthorizeAsync(userId, cancellationToken);
            if (actor.IsFailure) return actor.Error;
            var context = AiAssistantChannelPolicy.HomeDocument(userId, request.Culture, actor.Value.TimeZoneId);
            var name = AiAssistantHomeAttachmentPolicy.Name(request.FileName);
            var existing = await operations.GetAsync(context, request.RequestId, cancellationToken);
            if (existing.IsSuccess) return Recovered(existing.Value, request, name);
            if (existing.Error != AiAssistantErrors.NotFound) return existing.Error;
            if (await documents.CheckAsync(userId, cancellationToken) is { } refused) return refused;
            var prepared = await documents.PrepareAsync(request.Content, name, cancellationToken);
            if (prepared.IsFailure) return prepared.Error;
            var now = clock.GetUtcNow().UtcDateTime;
            var expiresAtUtc = now.AddDays(limits.RetentionDays);
            var descriptor = AiAssistantHomeDocumentPolicy.Describe(request.RequestId, request.Content, name, prepared.Value, expiresAtUtc);
            if (descriptor.IsFailure) return descriptor.Error;
            var stored = await documents.StoreAsync(descriptor.Value, request.Content, cancellationToken);
            if (stored.IsFailure) return stored.Error;
            var accepted = await operations.SubmitAsync(new AssistantOperationRequest(request.RequestId, context, "", now, expiresAtUtc)
                { Document = descriptor.Value }, cancellationToken);
            if (accepted.IsSuccess) return new AiAssistantDocumentResponse(request.RequestId, accepted.Value.State, null, null, limits.PollingAfterSeconds);
            // Another upload of the same request got there first with this same copy, sealed at another instant: its reading answers both.
            if (accepted.Error != AiAssistantErrors.RequestReused) return accepted.Error;
            var raced = await operations.GetAsync(context, request.RequestId, cancellationToken);
            return raced.IsSuccess ? Recovered(raced.Value, request, name) : accepted.Error;
        });

    public Task<Result<AiAssistantDocumentResponse>> GetAsync(Guid requestId, string culture, CancellationToken cancellationToken) =>
        OperationLog.RunAsync<Result<AiAssistantDocumentResponse>>(logger, "GetAiAssistantDocument", async () =>
        {
            if (guard.CurrentUserId is not { } userId) return AiAssistantErrors.SessionRequired;
            var actor = await guard.AuthorizeAsync(userId, cancellationToken);
            if (actor.IsFailure) return actor.Error;
            var found = await operations.GetAsync(AiAssistantChannelPolicy.HomeDocument(userId, culture, actor.Value.TimeZoneId), requestId, cancellationToken);
            return found.IsFailure ? found.Error : Response(found.Value, culture);
        });

    // A retry is the same file (SHA-256, size and name) as the reading's, in any state; another file under the request is a conflict. A
    // reading whose content is gone has no descriptor to compare and nothing left to give, so it answers as it is.
    private Result<AiAssistantDocumentResponse> Recovered(AssistantOperationResponse operation, SubmitAiAssistantDocumentRequest request, string name) =>
        operation.Document is { } document && !AiAssistantHomeDocumentPolicy.IsSameFile(document, request.Content, name)
            ? AiAssistantErrors.RequestReused : Response(operation, request.Culture);

    // The document and how readable it was only once the reading completed; a failure says why and offers a new attempt.
    private AiAssistantDocumentResponse Response(AssistantOperationResponse operation, string culture) => new(operation.RequestId, operation.State,
        operation.Document is { } document && operation.DocumentReading is { } reading ? AiAssistantHomeDocumentPolicy.Response(document, reading) : null,
        operation.FailureCode is { } code ? new AiAssistantFailure(code, ErrorMessages.Find(code) ?? AssistantTexts.Get(code, culture), "newAttempt") : null,
        limits.PollingAfterSeconds);
}
