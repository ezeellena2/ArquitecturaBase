using ArquitecturaBase.Application.Common.Validation;
using ArquitecturaBase.Application.Interfaces.Channels;
using ArquitecturaBase.Application.Interfaces.Persistence;
using ArquitecturaBase.Application.Modules.BusinessForms.Interfaces.Persistence;
using ArquitecturaBase.Application.Modules.BusinessForms.Interfaces.Services;
using ArquitecturaBase.Application.Modules.BusinessForms.Models;
using ArquitecturaBase.Domain.Modules.BusinessForms;
using ArquitecturaBase.Domain.Modules.BusinessForms.Documents;
using ArquitecturaBase.Domain.Results;

namespace ArquitecturaBase.Application.Modules.BusinessForms.Services;

/// <summary>
/// Changing a received document's form, waited for like a dictation (decision 15). Asking checks «Revisar documentos», the state (never
/// while its person can still complete it), the reading and the expected revision, submits the extraction outside any unit of work and
/// then records it under the document's lock as the one the document waits for, with who asked, in which language and zone; the same
/// request again is answered without submitting, and with another form it is a conflict. Reading it asks the engine with that sealed
/// context, whoever polls, and once it finished applies it in one unit of work: proposed for the fixed form, or the request dropped with
/// its failure code. An engine that refuses or fails answers its own error and leaves the request as it was, except one that refuses to
/// show it for good while it is read (403): then the request is dropped, so the document is not left reading for everybody. Each step goes
/// to the history; the change and its failure, as steps of whoever asked.
/// </summary>
internal sealed class FormReceivedDocumentExtractionService(FormReceivedDocumentAccessGuard guard, IFormReceivedDocumentReader reader,
    IFormReceivedDocumentRepository repository, IAssistantOperations operations, FormReceivedDocumentExtractionPolicy extractions,
    FormReceivedDocumentRecorder recorder, IUnitOfWork unitOfWork, TimeProvider clock) : IFormReceivedDocumentExtractionService
{
    public const string Unrecognized = FormReceivedDocumentExtractionPolicy.Unrecognized;

    public async Task<Result<FormReceivedDocumentExtractionResponse>> RequestAsync(Guid documentId, ChangeFormReceivedDocumentFormRequest request,
        CancellationToken cancellationToken)
    {
        var actor = await guard.CheckAsync(BusinessFormsPermissions.DocumentsReview, cancellationToken);
        if (actor.IsFailure) return actor.Error;
        if (request.RequestId is not { } requestId || requestId == Guid.Empty) return FieldErrors.On(FormReceivedDocumentErrors.ContentInvalid, "requestId");
        if (request.DefinitionId is not { } definitionId || definitionId == Guid.Empty) return FieldErrors.On(FormReceivedDocumentErrors.ContentInvalid, "definitionId");
        if (request.ExpectedRevision is not (>= 1 and <= 9007199254740991)) return FieldErrors.On(FormReceivedDocumentErrors.ContentInvalid, "expectedRevision");
        if (await reader.FindAsync(documentId, cancellationToken) is not { } row) return FormReceivedDocumentErrors.NotFound;
        // The same request again is answered as it stands, before looking at the revision; the same UUID for another form is not the same request.
        if (row.ExtractionRequestId == requestId)
            return row.ExtractionDefinitionId == definitionId ? Pending(requestId) : FormReceivedDocumentErrors.RequestConflict;
        if (row.State is not (FormReceivedDocumentState.Proposed or FormReceivedDocumentState.Unclassified))
            return FormReceivedDocumentErrors.StateConflict;
        var now = clock.GetUtcNow().UtcDateTime;
        if (row.State == FormReceivedDocumentState.Proposed && !FormReceivedDocument.IsForReview(row.State, row.NotSendable, row.ReplyExpiresAtUtc, now))
            return FormReceivedDocumentErrors.AwaitingSender;
        if (row.Revision != request.ExpectedRevision) return FormReceivedDocumentErrors.RevisionConflict;
        if (await reader.FindActiveFormAsync(definitionId, cancellationToken) is not { } form) return FormReceivedDocumentErrors.FormUnavailable;
        var context = FormReceivedDocumentExtractionPolicy.Context(actor.Value.Id, actor.Value.Culture, actor.Value.TimeZoneId, definitionId);
        if (extractions.Request(row, requestId, context, now) is not { } operation) return FormReceivedDocumentErrors.ReadingUnavailable;
        var submitted = await operations.SubmitAsync(operation, cancellationToken);
        if (submitted.IsFailure) return EngineError(submitted.Error);
        var saved = await unitOfWork.ExecuteInTransactionAsync<Result>(async ct =>
        {
            await repository.LockAsync(documentId, ct);
            if (await repository.GetAsync(documentId, ct) is not { } document) return FormReceivedDocumentErrors.NotFound;
            if (document.ExtractionRequestId == requestId)
                return document.ExtractionDefinitionId == definitionId ? Result.Success() : FormReceivedDocumentErrors.RequestConflict;
            if (!await guard.HasAsync(actor.Value.Id, BusinessFormsPermissions.DocumentsReview, ct)) return FormDefinitionErrors.Forbidden;
            var requested = document.RequestExtraction(request.ExpectedRevision.Value, requestId, definitionId, actor.Value.Id, context.Culture,
                context.TimeZoneId, now);
            return requested.IsFailure ? requested
                : recorder.Record(document.Id, FormReceivedDocumentEventKind.ExtractionRequested, actor.Value.Id, FormDocumentFormPolicy.Name(form, actor.Value.Culture));
        }, CommitPolicy.OnSuccess, cancellationToken);
        return saved.IsFailure ? saved.Error : Pending(requestId);
    }

    public async Task<Result<FormReceivedDocumentExtractionResponse>> GetAsync(Guid documentId, Guid requestId, CancellationToken cancellationToken)
    {
        var actor = await guard.CheckAsync(BusinessFormsPermissions.DocumentsReview, cancellationToken);
        if (actor.IsFailure) return actor.Error;
        if (await reader.FindAsync(documentId, cancellationToken) is not { } row) return FormReceivedDocumentErrors.NotFound;
        if (requestId == Guid.Empty || row.ExtractionRequestId != requestId || row.ExtractionDefinitionId is not { } definitionId
            || FormReceivedDocumentExtractionPolicy.Sealed(row) is not { } context)
            return FormReceivedDocumentErrors.ExtractionNotFound;
        // Read with the context it was asked with, whoever polls now: another reviewer, or the same one in another language.
        var found = await operations.GetAsync(context, requestId, cancellationToken);
        // An engine that refuses it for good (403) drops it, so the document is not left reading for everybody, and the refusal is
        // answered; a transient failure (5xx, network, 429) leaves it as it was.
        var refused = found.IsFailure && found.Error.Type == ErrorType.Forbidden;
        if (found.IsFailure && !refused && found.Error.Type != ErrorType.NotFound) return EngineError(found.Error);
        var operation = found.IsSuccess ? found.Value : null;
        if (operation is { State: "pending" or "running" }) return Pending(requestId);
        var outcome = operation is { State: "completed" } ? await extractions.PrepareAsync(operation, row, definitionId, cancellationToken) : null;
        var failure = refused ? FormReceivedDocumentExtractionPolicy.Refused
            : outcome is { Proposal: not null } ? null
            : operation is null ? FormReceivedDocumentErrors.ExtractionNotFound.Code
            : outcome?.FailureCode ?? operation.FailureCode ?? Unrecognized;
        var saved = await unitOfWork.ExecuteInTransactionAsync<Result>(async ct =>
        {
            await repository.LockAsync(documentId, ct);
            if (await repository.GetAsync(documentId, ct) is not { } document) return FormReceivedDocumentErrors.NotFound;
            if (document.ExtractionRequestId != requestId) return refused ? Result.Success() : FormReceivedDocumentErrors.ExtractionNotFound;
            // The step is of whoever asked for the change, in their language, even when another reviewer's poll applies it.
            var asker = document.ExtractionActorUserId ?? actor.Value.Id;
            var askerCulture = document.ExtractionCulture ?? actor.Value.Culture;
            if (failure is not null)
            {
                var failed = document.FailExtraction(requestId);
                return failed.IsFailure ? failed : recorder.Record(document.Id, FormReceivedDocumentEventKind.ExtractionFailed, asker, failure);
            }
            var prepared = outcome!.Proposal!;
            var applied = document.ApplyExtraction(requestId, prepared.Candidates[0].VersionId, extractions.Protect(document.Id, prepared), prepared.DocumentType,
                FormReceivedDocumentSearchPolicy.Build(extractions.Reading(document.Id, document.ProtectedReading), prepared));
            return applied.IsFailure ? applied
                : recorder.Record(document.Id, FormReceivedDocumentEventKind.FormChanged, asker, FormDocumentFormPolicy.Name(outcome.Form!, askerCulture));
        }, CommitPolicy.OnSuccess, cancellationToken);
        if (saved.IsFailure) return saved.Error;
        if (refused) return FormDefinitionErrors.Forbidden;
        return new FormReceivedDocumentExtractionResponse(requestId, failure is null ? "completed" : "failed", failure);
    }

    private static FormReceivedDocumentExtractionResponse Pending(Guid requestId) => new(requestId, "pending", null);

    // What the engine refused or could not do, as the inbox's own errors: never the engine's codes.
    private static Error EngineError(Error error) =>
        error.Type == ErrorType.Forbidden ? FormDefinitionErrors.Forbidden : FormReceivedDocumentErrors.ExtractionUnavailable;
}
