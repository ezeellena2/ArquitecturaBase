using ArquitecturaBase.Application.Common.Validation;
using ArquitecturaBase.Application.Models.StructuredData;
using ArquitecturaBase.Application.Modules.BusinessForms.Interfaces.Persistence;
using ArquitecturaBase.Application.Modules.BusinessForms.Interfaces.Services;
using ArquitecturaBase.Application.Modules.BusinessForms.Models;
using ArquitecturaBase.Domain.Modules.BusinessForms;
using ArquitecturaBase.Domain.Modules.BusinessForms.Documents;
using ArquitecturaBase.Domain.Results;

namespace ArquitecturaBase.Application.Modules.BusinessForms.Services;

/// <summary>
/// «Pasar al formulario» (DOC5-A, decision 15) with «Revisar documentos»: a repeated request returns its first result, before looking at
/// the state or the revision; a new one needs the document proposed for the form of the answered version and no longer waiting for its
/// person, its expected revision, its sender still active and the form's active version. The capture opens the unit of work, checks the
/// revision, the form and the waiting again under the document's lock and confirms the document in it; the response is in the sender's
/// account language.
/// </summary>
internal sealed class FormReceivedDocumentRecordService(FormReceivedDocumentAccessGuard guard, IFormReceivedDocumentReader reader, IFormEntryReader entries,
    FormReceivedDocumentCapturePolicy captures, TimeProvider clock) : IFormReceivedDocumentRecordService
{
    // What the capture rejected under the document's lock, as the inbox's own errors.
    private static readonly Dictionary<string, Error> LockedRejections = new[]
    {
        FormReceivedDocumentErrors.StateConflict, FormReceivedDocumentErrors.RevisionConflict, FormReceivedDocumentErrors.AwaitingSender,
    }.ToDictionary(error => error.Code);

    public async Task<Result<FormReceivedDocumentRecordResponse>> PassAsync(Guid documentId, PassFormReceivedDocumentRequest request, CancellationToken cancellationToken)
    {
        var actor = await guard.CheckAsync(BusinessFormsPermissions.DocumentsReview, cancellationToken);
        if (actor.IsFailure) return actor.Error;
        if (request.RequestId is not { } requestId || requestId == Guid.Empty) return FieldErrors.On(FormReceivedDocumentErrors.ContentInvalid, "requestId");
        if (request.VersionId is not { } versionId || versionId == Guid.Empty) return FieldErrors.On(FormReceivedDocumentErrors.ContentInvalid, "versionId");
        if (request.ExpectedRevision is not (>= 1 and <= 9007199254740991)) return FieldErrors.On(FormReceivedDocumentErrors.ContentInvalid, "expectedRevision");
        if (request.Answers is null || request.Answers.Count > 200 || request.Answers.Any(answer => answer is null || answer.Rows?.Any(row => row is null || row.Any(item => item is null)) == true))
            return FieldErrors.On(FormReceivedDocumentErrors.ContentInvalid, "answers");
        if (await reader.FindAsync(documentId, cancellationToken) is not { } row) return FormReceivedDocumentErrors.NotFound;
        if (row.DefinitionId is not { } definitionId) return FormReceivedDocumentErrors.StateConflict;
        if (await entries.FindDefinitionAsync(definitionId, versionId, cancellationToken) is not { Version: { } version } definition)
            return FormReceivedDocumentErrors.FormUnavailable;
        var prepared = await captures.PrepareAsync(row, definition, version, actor.Value.Id, request, cancellationToken);
        if (prepared.IsFailure) return prepared.Error;
        if (await captures.FindExistingAsync(prepared.Value, cancellationToken) is { } prior)
            return await ResponseAsync(row, definition.Prefix, prior, prepared.Value.Group, cancellationToken);
        if (row.State != FormReceivedDocumentState.Proposed) return FormReceivedDocumentErrors.StateConflict;
        if (!FormReceivedDocument.IsForReview(row.State, row.NotSendable, row.ReplyExpiresAtUtc, clock.GetUtcNow().UtcDateTime))
            return FormReceivedDocumentErrors.AwaitingSender;
        if (row.Revision != request.ExpectedRevision) return FormReceivedDocumentErrors.RevisionConflict;
        if (await guard.FindActiveAsync(row.SenderUserId, cancellationToken) is not { } sender) return FormReceivedDocumentErrors.SenderInactive;
        if (definition.Deactivated) return FormEntryErrors.Unavailable;
        if (version.VersionId != definition.ActiveVersionId) return FormEntryErrors.VersionChanged;
        var captured = await captures.CaptureAsync(prepared.Value, definition, version, sender, cancellationToken);
        return captured.IsFailure ? captured.Error : await ResponseAsync(row, definition.Prefix, captured.Value, prepared.Value.Group, cancellationToken);
    }

    private async Task<Result<FormReceivedDocumentRecordResponse>> ResponseAsync(FormReceivedDocumentStoredRow row, string prefix,
        StructuredRecordCaptureResponse captured, Guid? group, CancellationToken ct)
    {
        if (captured is { State: "Rejected", ErrorCode: { } code } && LockedRejections.TryGetValue(code, out var rejection)) return rejection;
        var outcome = await captures.OutcomeAsync(prefix, captured, group, ct);
        return outcome.IsFailure ? outcome.Error : new FormReceivedDocumentRecordResponse(row.Id, outcome.Value.RecordId, outcome.Value.Number, outcome.Value.State);
    }
}
