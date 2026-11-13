using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using ArquitecturaBase.Application.Models.StructuredData;
using ArquitecturaBase.Application.Interfaces.Persistence;
using ArquitecturaBase.Application.Common.Logging;
using ArquitecturaBase.Application.Modules.BusinessForms.Interfaces.Persistence;
using ArquitecturaBase.Application.Modules.BusinessForms.Interfaces.Services;
using ArquitecturaBase.Application.Modules.BusinessForms.Models;
using ArquitecturaBase.Domain.Modules.BusinessForms;
using ArquitecturaBase.Domain.Results;
using Microsoft.Extensions.Logging;

namespace ArquitecturaBase.Application.Modules.BusinessForms.Services;

internal sealed class BusinessFormRecordRecorder(IFormRecordRepository records, FormRecordCaptureGuard guard,
    IUnitOfWork unitOfWork, TimeProvider timeProvider,
    ILogger<BusinessFormRecordRecorder> logger, BusinessFormFileRecorder fileRecorder, FormActionRunRecorder actions, FormRecordCaptureOriginPolicy origins) : IBusinessFormRecordCaptureService
{
    public Task<StructuredRecordCaptureResponse> CaptureAsync(StructuredRecordCaptureRequest request, CancellationToken cancellationToken) =>
        CaptureAsync(request, CaptureMode.Channel, null, cancellationToken);

    public Task<StructuredRecordCaptureResponse> CaptureEntryAsync(StructuredRecordCaptureRequest request, CancellationToken cancellationToken) =>
        CaptureAsync(request, CaptureMode.Entry, null, cancellationToken);

    public Task<StructuredRecordCaptureResponse?> FindExistingReceiptAsync(StructuredRecordCaptureRequest request,
        CancellationToken cancellationToken) => FindExistingAsync(request, CaptureMode.Channel, null, cancellationToken);

    public Task<StructuredRecordCaptureResponse?> FindExistingEntryAsync(StructuredRecordCaptureRequest request,
        CancellationToken cancellationToken) => FindExistingAsync(request, CaptureMode.Entry, null, cancellationToken);

    public Task<StructuredRecordCaptureResponse> CaptureCorrectionAsync(StructuredRecordCaptureRequest request, FormRecordCorrection correction,
        CancellationToken cancellationToken) => CaptureAsync(request, CaptureMode.Entry, correction, cancellationToken);

    public Task<StructuredRecordCaptureResponse?> FindExistingCorrectionAsync(StructuredRecordCaptureRequest request, FormRecordCorrection correction,
        CancellationToken cancellationToken) => FindExistingAsync(request, CaptureMode.Entry, correction, cancellationToken);

    public Task<StructuredRecordCaptureResponse> CaptureDocumentAsync(StructuredRecordCaptureRequest request, CancellationToken cancellationToken) =>
        CaptureAsync(request, CaptureMode.Document, null, cancellationToken);

    public Task<StructuredRecordCaptureResponse?> FindExistingDocumentAsync(StructuredRecordCaptureRequest request, CancellationToken cancellationToken) =>
        FindExistingAsync(request, CaptureMode.Document, null, cancellationToken);

    // How a response arrives: through a channel, loaded in the app (also a correction) or a received document a reviewer passed to its form
    // (task 7), which is not a response loaded in the app: the inbox gives its origin.
    private enum CaptureMode { Channel, Entry, Document }

    private async Task<StructuredRecordCaptureResponse> CaptureAsync(StructuredRecordCaptureRequest request, CaptureMode mode, FormRecordCorrection? correction,
        CancellationToken cancellationToken)
    {
        OperationLog.Handling(logger, "CaptureBusinessFormRecord");
        if (request.SourceReceiptId == Guid.Empty || request.ActorUserId == Guid.Empty
            || request.SubmittedAtUtc.Kind != DateTimeKind.Utc)
        {
            OperationLog.Failed(logger, "CaptureBusinessFormRecord", FormRecordErrors.AuthorizationChanged.Code);
            return new("Rejected", null, null, FormRecordErrors.AuthorizationChanged.Code);
        }

        var replay = await FindExistingAsync(request, mode, correction, cancellationToken);
        if (replay is not null)
        {
            if (replay.State == "Completed") OperationLog.Handled(logger, "CaptureBusinessFormRecord");
            else OperationLog.Failed(logger, "CaptureBusinessFormRecord", replay.ErrorCode ?? FormRecordErrors.InvalidResponse.Code);
            return replay;
        }
        if (!await CanCaptureAsync(request, mode, correction, cancellationToken))
        {
            OperationLog.Failed(logger, "CaptureBusinessFormRecord", FormRecordErrors.AuthorizationChanged.Code);
            return new("Rejected", null, null, FormRecordErrors.AuthorizationChanged.Code);
        }

        var prepared = request.Catalogs?.Any(FormCatalogOfferPolicy.InvalidValues) == true
            ? Result.Failure<IReadOnlyList<PreparedFormFile>>(FormRecordErrors.InvalidResponse)
            : await fileRecorder.PrepareAsync(request, cancellationToken);
        var result = (await unitOfWork.ExecuteInTransactionAsync(
            ct => CaptureCoreAsync(request, mode, correction, prepared, ct), CommitPolicy.OnSuccess, cancellationToken)).Value;
        if (result.State == "Completed") OperationLog.Handled(logger, "CaptureBusinessFormRecord");
        else OperationLog.Failed(logger, "CaptureBusinessFormRecord", result.ErrorCode ?? FormRecordErrors.InvalidResponse.Code);
        return result;
    }

    private async Task<StructuredRecordCaptureResponse?> FindExistingAsync(StructuredRecordCaptureRequest request, CaptureMode mode,
        FormRecordCorrection? correction, CancellationToken cancellationToken)
    {
        if (request.SourceReceiptId == Guid.Empty || request.ActorUserId == Guid.Empty
            || request.SubmittedAtUtc.Kind != DateTimeKind.Utc) return null;

        return await guard.FindExistingAsync(request, Fingerprint(request, mode, correction), cancellationToken);
    }

    // A correction is not a new response of whoever corrects: they correct with «Corregir y anular respuestas», or their own
    // response while nobody decided it. The response it replaces is checked under the lock. A received document is the response of whoever
    // sent it, still active, and the reviewer who passes it, the origin's operator, needs «Revisar documentos» instead of the sender's
    // permission to load responses.
    private async Task<bool> CanCaptureAsync(StructuredRecordCaptureRequest request, CaptureMode mode, FormRecordCorrection? correction, CancellationToken ct) =>
        correction is not null ? await guard.FindAmendAccessAsync(ct) is { } access && access.Actor == request.ActorUserId
        : mode == CaptureMode.Document ? request.Origin is { Document: not null, DocumentRevision: not null, OperatorUserId: { } reviewer }
            && await guard.CanCaptureDocumentAsync(request.ActorUserId, reviewer, ct)
        : await guard.CanCaptureAsync(request.ActorUserId, ct);

    private async Task<Result<StructuredRecordCaptureResponse>> CaptureCoreAsync(StructuredRecordCaptureRequest request, CaptureMode mode,
        FormRecordCorrection? correction, Result<IReadOnlyList<PreparedFormFile>> prepared, CancellationToken ct)
    {
        await records.LockSourceReceiptAsync(request.SourceReceiptId, ct);
        var fingerprint = Fingerprint(request, mode, correction);
        var prior = await records.GetReceiptAsync(request.SourceReceiptId, ct);
        if (prior is not null)
        {
            return prior.Fingerprint == fingerprint
                ? new StructuredRecordCaptureResponse(prior.State, prior.RecordId, prior.Number, prior.ErrorCode, prior.RejectedFieldId, prior.RejectedRow)
                : new StructuredRecordCaptureResponse("Rejected", null, null, FormRecordErrors.ReplayConflict.Code);
        }

        if (request.PresentationCulture is not (null or "es" or "en"))
            return Reject(request, fingerprint, FormRecordErrors.InvalidResponse.Code);

        if (!await CanCaptureAsync(request, mode, correction, ct))
            return Reject(request, fingerprint, FormRecordErrors.AuthorizationChanged.Code);

        await records.LockDefinitionsAsync(ct);
        var definition = await records.GetDefinitionAsync(request.DefinitionId, ct);
        var version = await records.GetVersionAsync(request.VersionId, ct);
        if (definition is null || version is null || version.DefinitionId != request.DefinitionId
            || (definition.ActiveVersionId != version.Id && !await records.ExistsPublishedAsync(version.Id, ct)))
            return Reject(request, fingerprint, FormRecordErrors.InvalidResponse.Code);

        BusinessFormRecord? replaced = null;
        FormRecordFileRow[] kept = [];
        if (correction is not null)
        {
            // Under the lock approvals take: the response is still current, undecided for its author, without a live issuance (one an
            // approval issued after the correction was opened is voided and loaded again), and the files kept are its own.
            replaced = await records.GetRecordAsync(correction.RecordId, ct);
            if (replaced is null || replaced.DefinitionId != definition.Id) return Reject(request, fingerprint, FormRecordErrors.NotFound.Code);
            var access = await guard.FindAmendAccessAsync(ct);
            var allowed = access is null ? FormDefinitionErrors.Forbidden
                : FormRecordAmendmentGuard.Allows(access, replaced.ActorUserId, replaced.State, replaced.ReplacedByRecordId is not null);
            if (allowed.IsFailure) return Reject(request, fingerprint, allowed.Error.Code);
            if (await guard.HasLiveIssuanceAsync(replaced.Id, ct)) return Reject(request, fingerprint, FormRecordErrors.IssuedNotAmendable.Code);
            var existing = await records.ListFilesAsync(replaced.Id, ct);
            kept = existing.Where(file => correction.KeptFileIds.Contains(file.FileId)).ToArray();
            if (kept.Length != correction.KeptFileIds.Count) return Reject(request, fingerprint, FormRecordErrors.InvalidResponse.Code);
        }

        var schema = FormDraftPolicy.Read<FormVersionSchema>(version.SchemaJson);
        if (prepared.IsFailure) return Reject(request, fingerprint, prepared.Error.Code, prepared.Error.Metadata?.GetValueOrDefault("fieldId") as Guid?);
        var files = prepared.Value;
        int Count(Guid fieldId) => files.Count(file => file.FieldId == fieldId) + kept.Count(file => file.FieldId == fieldId);
        if (files.Any(file => !schema.Questions.Any(question => question.FieldId == file.FieldId && question.Type == file.Type))
            || kept.Any(file => !schema.Questions.Any(question => question.FieldId == file.FieldId && question.Files is not null))
            || files.Count + kept.Length > 10
            || schema.Questions.Where(question => question.Files is not null).Any(question =>
                request.Answers.ContainsKey(question.Key) || Count(question.FieldId) < question.Files!.MinFiles
                || Count(question.FieldId) > question.Files!.MaxFiles))
            return Reject(request, fingerprint, FormRecordErrors.InvalidResponse.Code);
        var offers = request.Catalogs ?? [];
        if (offers.Select(offer => offer.FieldId).Distinct().Count() != offers.Count
            || offers.Any(offer => offer.Revision <= 0 || offer.Kind is not ("Manual" or "Filters" or FormCatalogKindPolicy.External)
                || !schema.Questions.Any(question => question.FieldId == offer.FieldId && question.Key == offer.Key && question.Source?.Catalog == offer.CatalogId)
                || FormCatalogOfferPolicy.Validate(false, offer.Options, grouping: offer.Grouping) is not null
                || FormCatalogOfferPolicy.InvalidNames(offer.CatalogNames) || FormCatalogOfferPolicy.InvalidValues(offer)))
            return Reject(request, fingerprint, FormRecordErrors.InvalidResponse.Code);
        var answers = FormRecordAnswerPolicy.Normalize(schema, request.Answers, offers);
        if (answers is null)
            return Reject(request, fingerprint, FormRecordErrors.InvalidResponse.Code);
        // DOC1: the fixed validations of the bound questions, at the question (and item) that does not pass them.
        if (FormRecordAnswerPolicy.FindInvalid(schema, answers) is { } invalid)
            return Reject(request, fingerprint, invalid.Error.Code, invalid.FieldId, invalid.Row).Value with { FixedValidation = true };

        // Each answer with its item of the block, and the options chosen as «¿La emito?» reads them (FormIssuePreviewPolicy).
        var placed = FormRecordReferencePolicy.Placed(answers).ToArray();
        var selected = FormRecordReferencePolicy.Chosen(schema, answers, offers);
        foreach (var catalogId in selected.Where(value => FormCatalogKindPolicy.HasItems(value.Offer.Kind)).Select(value => value.Offer.CatalogId).Distinct().Order())
            await records.LockCatalogAsync(catalogId, ct);
        // An option chosen in several items is checked once, where it first appears, which names its question and item.
        foreach (var value in selected.Where(value => FormCatalogKindPolicy.HasItems(value.Offer.Kind)).DistinctBy(value => (value.Offer.CatalogId, value.ChoiceId)))
            if (!await records.ExistsActiveCatalogItemAsync(value.Offer.CatalogId, value.ChoiceId, ct))
                return Reject(request, fingerprint, FormRecordErrors.CatalogItemInactive.Code, value.FieldId, value.Item);

        var catalogFields = await FormCatalogFieldCapturePolicy.CaptureAsync(records, FormRecordReferencePolicy.ItemIds(selected), ct);

        var origin = await origins.ValidateAsync(request, schema, ct);
        if (origin.IsFailure) return Reject(request, fingerprint, origin.Error.Code).Value with { Repetition = FormRecordRepetitionPolicy.Describe(origin.Error) };

        var valuesJson = FormDraftPolicy.Serialize(answers.Select(value => new { value.FieldId, value.Value }).ToArray());
        BusinessFormRecord record;
        if (replaced is null)
            record = BusinessFormRecord.Create(definition.Id, version.Id, definition.AllocateRecordNumber(), request.ActorUserId,
                request.SourceReceiptId, request.SubmittedAtUtc, valuesJson, definition.ActiveRuleSetId, request.PresentationCulture, mode == CaptureMode.Entry);
        else
        {
            // The correction keeps the number, author, reception and channel; the replaced response stops waiting for a decision.
            var corrected = replaced.Correct(version.Id, request.SourceReceiptId, valuesJson, definition.ActiveRuleSetId,
                request.PresentationCulture, request.ActorUserId, correction!.Reason);
            if (corrected.IsFailure) return Reject(request, fingerprint, corrected.Error.Code);
            record = corrected.Value;
            if (await records.GetOpenTaskAsync(replaced.Id, ct) is { } task && task.Close() is { IsFailure: true } closed)
                throw new InvalidOperationException("The open approval task of the corrected response could not close: " + closed.Error.Code);
        }
        var number = record.Number;
        if (origin.Value is { } captured)
        {
            var proposedReferences = captured.ProposedChoices.Select(choice => new FormRecordReferenceRow(record.Id,
                choice.Offer.FieldId, choice.Offer.CatalogId, choice.Offer.Revision, choice.Option.Id,
                choice.Option.Text, choice.Option.Detail, choice.Offer.CatalogName,
                FormRecordReferencePolicy.Capture(choice.Option, choice.Offer), choice.Option.Value, choice.Offer.ValueType)).ToArray();
            var snapshotOrigin = new FormRecordCaptureOriginSnapshot(captured.Origin, captured.ChangedFieldIds, proposedReferences);
            record.SetCaptureOrigin(captured.Origin.Suggestion?.OriginRecordId, captured.Origin.Suggestion?.OriginSuggestionId,
                FormDraftPolicy.Serialize(snapshotOrigin));
            // The received document it came from is confirmed with its number in this same commit (checked under its lock above).
            if (captured.Document is { } document)
                origins.ConfirmDocument(document, record.Id, definition.Id, version.Id, mode == CaptureMode.Document ? captured.Origin.OperatorUserId : null,
                    definition.Prefix + "-" + number.ToString("D6", System.Globalization.CultureInfo.InvariantCulture));
        }
        records.Add(record);
        foreach (var (value, item) in placed)
        {
            if (value.Type == "multiChoice")
            {
                for (var position = 0; position < value.ChoiceIds!.Length; position++)
                    records.Add(BusinessFormRecordValue.Create(record.Id, definition.Id, value.FieldId, value.Type,
                        choiceId: value.ChoiceIds[position], position: position, row: item ?? 0));
            }
            else if (value.Location is { } location)
            {
                // UB1-A: latitude at position 0, longitude at 1 and the accuracy, when it came, at 2.
                decimal?[] parts = [location.Latitude, location.Longitude, location.AccuracyMeters];
                for (var position = 0; position < parts.Length; position++)
                    if (parts[position] is { } part)
                        records.Add(BusinessFormRecordValue.Create(record.Id, definition.Id, value.FieldId, value.Type,
                            decimalValue: part, position: position, row: item ?? 0));
            }
            else
                records.Add(BusinessFormRecordValue.Create(record.Id, definition.Id, value.FieldId, value.Type,
                    value.Text, value.Decimal, value.Date, value.Boolean, value.ChoiceId, row: item ?? 0));
        }
        var references = FormRecordReferencePolicy.References(record.Id, selected, catalogFields);
        foreach (var reference in references)
            records.Add(BusinessFormRecordReference.Create(reference.RecordId, definition.Id, reference.FieldId, reference.CatalogId,
                reference.CatalogRevision, reference.ItemId, reference.Text, reference.Detail, reference.CatalogName, reference.TranslationsJson,
                reference.Value, reference.ValueType, reference.FieldValuesJson, reference.Row));
        var fileRows = fileRecorder.Record(record, files, kept);
        var stored = new FormRecordStoredRow(record.SourceReceiptId, record.Id, record.Number, definition.Id, definition.Prefix,
            version.Id, version.Number, version.NameJson ?? throw new InvalidOperationException("The captured form presentation is missing."),
            version.SchemaJson, record.ValuesJson, references, fileRows);
        var snapshot = new FormActionRecordRow(new(stored, record.SubmittedAtUtc, new(record.ActorUserId, null), timeProvider.GetUtcNow().UtcDateTime),
            version.Culture ?? throw new InvalidOperationException("The captured form culture is missing."));
        await actions.RecordAsync(record, snapshot, "Received", ct);
        records.Add(BusinessFormCaptureReceipt.Complete(request.SourceReceiptId, definition.Id, version.Id,
            fingerprint, record.Id, number, timeProvider.GetUtcNow().UtcDateTime));
        return new StructuredRecordCaptureResponse("Completed", record.Id, number, null);
    }

    private Result<StructuredRecordCaptureResponse> Reject(StructuredRecordCaptureRequest request, string fingerprint, string code,
        Guid? rejectedFieldId = null, int? rejectedRow = null)
    {
        records.Add(BusinessFormCaptureReceipt.Reject(request.SourceReceiptId, request.DefinitionId, request.VersionId,
            fingerprint, code, timeProvider.GetUtcNow().UtcDateTime, rejectedFieldId, rejectedRow));
        return new StructuredRecordCaptureResponse("Rejected", null, null, code, rejectedFieldId, rejectedRow);
    }

    // An entry's offer is rebuilt by the server on every send, so it stays out: the replay compares what the person sent.
    private static string Fingerprint(StructuredRecordCaptureRequest request, CaptureMode mode, FormRecordCorrection? correction)
    {
        var canonical = JsonSerializer.Serialize(new
        {
            request.SourceReceiptId, request.DefinitionId, request.VersionId, request.ActorUserId,
            Answers = request.Answers.OrderBy(pair => pair.Key, StringComparer.Ordinal).Select(pair => new { pair.Key, pair.Value }),
        });
        if (mode == CaptureMode.Entry) canonical += "app";
        // A received document's offer is rebuilt by the server too, for whoever sent it: its replay compares what the reviewer sent.
        else if (mode == CaptureMode.Document) canonical += "document";
        else if (request.Catalogs is { Count: > 0 }) canonical += JsonSerializer.Serialize(request.Catalogs.OrderBy(offer => offer.FieldId));
        if (correction is not null)
            canonical += "correction" + JsonSerializer.Serialize(new { correction.RecordId, correction.Reason, Kept = correction.KeptFileIds.Order() });
        if (request.Files is { Count: > 0 }) canonical += JsonSerializer.Serialize(request.Files.OrderBy(file => file.FieldId).ThenBy(file => file.Position));
        if (request.Origin is { } origin) canonical += JsonSerializer.Serialize(origin with
        {
            ProposedValues = origin.ProposedValues.OrderBy(value => value.FieldId).ToArray(),
            IncludedFieldIds = origin.IncludedFieldIds.Order().ToArray(),
            Suggestion = origin.Suggestion is { } suggestion ? suggestion with
            {
                Mappings = suggestion.Mappings.OrderBy(mapping => mapping.DestinationFieldId).ToArray()
            } : null
        });
        return Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(canonical)));
    }
}
