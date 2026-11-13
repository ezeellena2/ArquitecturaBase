using System.Globalization;
using ArquitecturaBase.Application.Common.Validation;
using ArquitecturaBase.Application.Interfaces.Channels;
using ArquitecturaBase.Application.Interfaces.Persistence;
using ArquitecturaBase.Application.Models.Assistant;
using ArquitecturaBase.Application.Modules.BusinessForms.Interfaces.Persistence;
using ArquitecturaBase.Application.Modules.BusinessForms.Interfaces.Services;
using ArquitecturaBase.Application.Modules.BusinessForms.Models;
using ArquitecturaBase.Domain.Modules.BusinessForms;
using ArquitecturaBase.Domain.Modules.BusinessForms.Documents;
using ArquitecturaBase.Domain.Results;

namespace ArquitecturaBase.Application.Modules.BusinessForms.Services;

/// <summary>
/// «Armar formulario con la IA» (DOC6, decision 17), waited for like a dictation. Asking checks «Revisar documentos» and «Diseñar
/// formularios», takes the documents (<see cref="FormDocumentAuthoringPolicy"/>), submits the interpretation outside any unit of work and then
/// keeps the request (who asked, in which language and zone, and its documents) in one; the same request again is answered without
/// submitting, and asking for something else under it is a conflict. What the engine already took under the request's UUID (another attempt
/// that got there first, or one whose save failed) is recovered, never run twice (ADR 0018). If the engine refuses or fails, nothing is kept.
/// Reading it asks the engine with that context: pending, completed with its proposal or failed with why, with its documents as the inbox
/// lists them, the topic of its group and the draft it created.
/// </summary>
internal sealed class FormDocumentAuthoringService(FormDocumentAuthoringPolicy policy, IFormDocumentProposalReader reader,
    IFormDocumentProposalRepository repository, IAssistantOperations operations, FormReceivedDocumentPresentationPolicy presentation,
    IUnitOfWork unitOfWork, TimeProvider clock) : IFormDocumentAuthoringService
{
    public async Task<Result<FormDocumentProposalResponse>> RequestAsync(RequestFormDocumentProposalRequest request, CancellationToken cancellationToken)
    {
        var actor = await policy.CheckAsync(cancellationToken);
        if (actor.IsFailure) return actor.Error;
        if (request.RequestId is not { } requestId || requestId == Guid.Empty) return FieldErrors.On(FormReceivedDocumentErrors.ContentInvalid, "requestId");
        if (request.DocumentId == Guid.Empty || request.TopicId == Guid.Empty || (request.DocumentId is null) == (request.TopicId is null))
            return FieldErrors.On(FormReceivedDocumentErrors.ContentInvalid, request.TopicId is null ? "documentId" : "topicId");
        // The same request again is answered as it stands; the same UUID for another document or group is not the same request.
        if (await reader.FindByRequestAsync(actor.Value.Id, requestId, cancellationToken) is { } prior)
            return FormDocumentProposal.Asks(prior.TopicId, prior.DocumentIds, request.DocumentId, request.TopicId)
                ? await ReadAsync(actor.Value.Id, prior, cancellationToken) : FormReceivedDocumentErrors.RequestConflict;
        var rows = request.DocumentId is { } documentId ? await policy.DocumentAsync(documentId, cancellationToken)
            : await policy.GroupAsync(request.TopicId!.Value, cancellationToken);
        if (rows.IsFailure) return rows.Error;
        var context = FormDocumentAuthoringPolicy.Context(actor.Value.Id, actor.Value.Culture, actor.Value.TimeZoneId, requestId);
        var taken = await TakeAsync(requestId, context, rows.Value, cancellationToken);
        if (taken.IsFailure) return taken.Error;
        var saved = await unitOfWork.ExecuteInTransactionAsync<Result>(async ct =>
        {
            await repository.LockRequestAsync(actor.Value.Id, requestId, ct);
            if (await repository.GetByRequestAsync(actor.Value.Id, requestId, ct) is { } existing)
                return existing.Asks(request.DocumentId, request.TopicId) ? Result.Success() : FormReceivedDocumentErrors.RequestConflict;
            var created = FormDocumentProposal.Request(requestId, actor.Value.Id, context.Culture, context.TimeZoneId, request.TopicId,
                rows.Value.Select(row => row.Id).ToArray(), taken.Value);
            if (created.IsFailure) return created.Error;
            repository.Add(created.Value);
            return Result.Success();
        }, CommitPolicy.OnSuccess, cancellationToken);
        if (saved.IsFailure) return saved.Error;
        if (await reader.FindByRequestAsync(actor.Value.Id, requestId, cancellationToken) is not { } kept) return FormReceivedDocumentErrors.ProposalNotFound;
        return await ReadAsync(actor.Value.Id, kept, cancellationToken);
    }

    // The engine runs a request once (ADR 0018). One it already took under this UUID, by another attempt that got there first or by one
    // whose save failed, is submitted again sealed at that same instant: the same documents are then a replay that runs nothing new, and other
    // documents a conflict. The instant the request is sealed at.
    private async Task<Result<DateTime>> TakeAsync(Guid requestId, AssistantChannelContext context, IReadOnlyList<FormReceivedDocumentStoredRow> rows,
        CancellationToken ct)
    {
        var known = await operations.GetAsync(context, requestId, ct);
        if (known.IsFailure && known.Error.Type != ErrorType.NotFound) return EngineError(known.Error);
        if (known.IsFailure)
        {
            var nowUtc = clock.GetUtcNow().UtcDateTime;
            var submitted = await SubmitAsync(requestId, context, rows, nowUtc, ct);
            if (submitted.IsSuccess) return nowUtc;
            if (!IsReused(submitted.Error)) return submitted.Error == FormReceivedDocumentErrors.ReadingUnavailable ? submitted.Error : EngineError(submitted.Error);
            // A conflict of the engine is the UUID taken before only when the lookup now finds it in this context; otherwise (the assistant
            // paused, not configured or not qualified, or the UUID taken where this lookup cannot see it) it is the engine's own failure.
            known = await operations.GetAsync(context, requestId, ct);
            if (known.IsFailure) return EngineError(known.Error.Type == ErrorType.NotFound ? submitted.Error : known.Error);
        }
        if (known.Value.Provenance?.OccurredAtUtc is not { Kind: DateTimeKind.Utc } sealedAtUtc) return FormReceivedDocumentErrors.ProposalUnavailable;
        var replayed = await SubmitAsync(requestId, context, rows, sealedAtUtc, ct);
        return replayed.IsSuccess ? sealedAtUtc
            : IsReused(replayed.Error) ? FormReceivedDocumentErrors.RequestConflict
            : replayed.Error == FormReceivedDocumentErrors.ReadingUnavailable ? replayed.Error : EngineError(replayed.Error);
    }

    private async Task<Result> SubmitAsync(Guid requestId, AssistantChannelContext context, IReadOnlyList<FormReceivedDocumentStoredRow> rows,
        DateTime sealedAtUtc, CancellationToken ct)
    {
        if (policy.Request(requestId, context, rows, sealedAtUtc) is not { } operation) return FormReceivedDocumentErrors.ReadingUnavailable;
        var submitted = await operations.SubmitAsync(operation, ct);
        return submitted.IsSuccess ? Result.Success() : submitted.Error;
    }

    // A conflict the engine does not offer to retry, unlike a turn still active: the UUID taken before (or the assistant not available).
    private static bool IsReused(Error error) => error.Type == ErrorType.Conflict && error.Metadata?.ContainsKey("retryable") != true;

    public async Task<Result<FormDocumentProposalResponse>> GetAsync(Guid requestId, CancellationToken cancellationToken)
    {
        var actor = await policy.CheckAsync(cancellationToken);
        if (actor.IsFailure) return actor.Error;
        if (requestId == Guid.Empty || await reader.FindByRequestAsync(actor.Value.Id, requestId, cancellationToken) is not { } row)
            return FormReceivedDocumentErrors.ProposalNotFound;
        return await ReadAsync(actor.Value.Id, row, cancellationToken);
    }

    // Read with the context it was asked with: an engine that refuses to show it answers 403, one that does not know it any more makes it
    // failed, and any other failure is the request's own error.
    private async Task<Result<FormDocumentProposalResponse>> ReadAsync(Guid actorUserId, FormDocumentProposalStoredRow row, CancellationToken ct)
    {
        var found = await operations.GetAsync(FormDocumentAuthoringPolicy.Context(row), row.RequestId, ct);
        if (found.IsFailure && found.Error.Type == ErrorType.Forbidden) return FormDefinitionErrors.Forbidden;
        if (found.IsFailure && found.Error.Type != ErrorType.NotFound) return FormReceivedDocumentErrors.ProposalUnavailable;
        var operation = found.IsSuccess ? found.Value : null;
        var proposal = operation is { State: "completed" } ? FormDocumentAuthoringPolicy.Proposal(operation) : null;
        (string State, string? Failure) outcome = operation switch
        {
            null => ("failed", FormReceivedDocumentErrors.ProposalNotFound.Code),
            { State: "pending" or "running" } => ("pending", null),
            { State: "completed" } when proposal is not null => ("completed", null),
            { State: "completed" } => ("failed", FormDocumentAuthoringPolicy.NoProposal),
            _ => ("failed", operation.FailureCode ?? FormDocumentAuthoringPolicy.NoProposal),
        };
        return await RespondAsync(actorUserId, row, outcome.State, outcome.Failure, proposal, clock.GetUtcNow().UtcDateTime, ct);
    }

    // The request as the page shows it, in the reader's language, with the permissions checked again after reading its documents.
    private async Task<Result<FormDocumentProposalResponse>> RespondAsync(Guid actorUserId, FormDocumentProposalStoredRow row, string state, string? failure,
        AssistantObservedProposal? proposal, DateTime nowUtc, CancellationToken ct)
    {
        var stored = await reader.ListDocumentsAsync(row.DocumentIds, ct);
        var ordered = row.DocumentIds.Select(id => stored.FirstOrDefault(document => document.Id == id)).OfType<FormReceivedDocumentStoredRow>().ToArray();
        var documents = await presentation.ItemsAsync(ordered, Culture(), nowUtc, ct);
        var response = new FormDocumentProposalResponse(row.RequestId, state, failure,
            row is { TopicId: { } topicId, TopicName: { } topicName } ? new FormReceivedDocumentTopicResponse(topicId, topicName) : null, documents,
            proposal is { Payload: FormAuthoringDocumentFormProposal content }
                ? new FormDocumentProposalContentResponse(proposal.Id, proposal.Capability.Id, proposal.Capability.Version, content) : null,
            row.DefinitionId);
        return await policy.HasAsync(actorUserId, ct) ? response : FormDefinitionErrors.Forbidden;
    }

    // What the engine refused or could not do, as the inbox's own errors, except its quota and capacity, which come back as they are (429
    // with its code, text and retryAfter), as in the other routes of the assistant. A failed proposal's failureCode is a different thing:
    // NoFormProposal, ProposalNotFound or the engine's own code, which the page shows as a generic failure.
    private static Error EngineError(Error error) => error.Type switch
    {
        ErrorType.Forbidden => FormDefinitionErrors.Forbidden,
        ErrorType.TooManyRequests => error,
        _ => FormReceivedDocumentErrors.ProposalUnavailable,
    };

    private static string Culture() => CultureInfo.CurrentUICulture.TwoLetterISOLanguageName == "en" ? "en" : "es";
}
