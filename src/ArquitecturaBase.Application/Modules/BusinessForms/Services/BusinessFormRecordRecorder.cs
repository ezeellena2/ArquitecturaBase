using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using ArquitecturaBase.Domain.Common;
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
        CaptureAsync(request, false, null, cancellationToken);

    public Task<StructuredRecordCaptureResponse> CaptureEntryAsync(StructuredRecordCaptureRequest request, CancellationToken cancellationToken) =>
        CaptureAsync(request, true, null, cancellationToken);

    public Task<StructuredRecordCaptureResponse?> FindExistingReceiptAsync(StructuredRecordCaptureRequest request,
        CancellationToken cancellationToken) => FindExistingAsync(request, false, null, cancellationToken);

    public Task<StructuredRecordCaptureResponse?> FindExistingEntryAsync(StructuredRecordCaptureRequest request,
        CancellationToken cancellationToken) => FindExistingAsync(request, true, null, cancellationToken);

    public Task<StructuredRecordCaptureResponse> CaptureCorrectionAsync(StructuredRecordCaptureRequest request, FormRecordCorrection correction,
        CancellationToken cancellationToken) => CaptureAsync(request, true, correction, cancellationToken);

    public Task<StructuredRecordCaptureResponse?> FindExistingCorrectionAsync(StructuredRecordCaptureRequest request, FormRecordCorrection correction,
        CancellationToken cancellationToken) => FindExistingAsync(request, true, correction, cancellationToken);

    private async Task<StructuredRecordCaptureResponse> CaptureAsync(StructuredRecordCaptureRequest request, bool entry, FormRecordCorrection? correction,
        CancellationToken cancellationToken)
    {
        OperationLog.Handling(logger, "CaptureBusinessFormRecord");
        if (request.SourceReceiptId == Guid.Empty || request.ActorUserId == Guid.Empty
            || request.SubmittedAtUtc.Kind != DateTimeKind.Utc)
        {
            OperationLog.Failed(logger, "CaptureBusinessFormRecord", FormRecordErrors.AuthorizationChanged.Code);
            return new("Rejected", null, null, FormRecordErrors.AuthorizationChanged.Code);
        }

        var replay = await FindExistingAsync(request, entry, correction, cancellationToken);
        if (replay is not null)
        {
            if (replay.State == "Completed") OperationLog.Handled(logger, "CaptureBusinessFormRecord");
            else OperationLog.Failed(logger, "CaptureBusinessFormRecord", replay.ErrorCode ?? FormRecordErrors.InvalidResponse.Code);
            return replay;
        }
        if (!await CanCaptureAsync(request, correction, cancellationToken))
        {
            OperationLog.Failed(logger, "CaptureBusinessFormRecord", FormRecordErrors.AuthorizationChanged.Code);
            return new("Rejected", null, null, FormRecordErrors.AuthorizationChanged.Code);
        }

        var prepared = request.Catalogs?.Any(FormCatalogOfferPolicy.InvalidValues) == true
            ? Result.Failure<IReadOnlyList<PreparedFormFile>>(FormRecordErrors.InvalidResponse)
            : await fileRecorder.PrepareAsync(request, cancellationToken);
        var result = (await unitOfWork.ExecuteInTransactionAsync(
            ct => CaptureCoreAsync(request, entry, correction, prepared, ct), CommitPolicy.OnSuccess, cancellationToken)).Value;
        if (result.State == "Completed") OperationLog.Handled(logger, "CaptureBusinessFormRecord");
        else OperationLog.Failed(logger, "CaptureBusinessFormRecord", result.ErrorCode ?? FormRecordErrors.InvalidResponse.Code);
        return result;
    }

    private async Task<StructuredRecordCaptureResponse?> FindExistingAsync(StructuredRecordCaptureRequest request, bool entry,
        FormRecordCorrection? correction, CancellationToken cancellationToken)
    {
        if (request.SourceReceiptId == Guid.Empty || request.ActorUserId == Guid.Empty
            || request.SubmittedAtUtc.Kind != DateTimeKind.Utc) return null;

        return await guard.FindExistingAsync(request, Fingerprint(request, entry, correction), cancellationToken);
    }

    // A correction is not a new response of whoever corrects: they correct with «Corregir y anular respuestas», or their own
    // response while nobody decided it. The response it replaces is checked under the lock.
    private async Task<bool> CanCaptureAsync(StructuredRecordCaptureRequest request, FormRecordCorrection? correction, CancellationToken ct) =>
        correction is null ? await guard.CanCaptureAsync(request.ActorUserId, ct)
            : await guard.FindAmendAccessAsync(ct) is { } access && access.Actor == request.ActorUserId;

    private async Task<Result<StructuredRecordCaptureResponse>> CaptureCoreAsync(StructuredRecordCaptureRequest request, bool entry,
        FormRecordCorrection? correction, Result<IReadOnlyList<PreparedFormFile>> prepared, CancellationToken ct)
    {
        await records.LockSourceReceiptAsync(request.SourceReceiptId, ct);
        var fingerprint = Fingerprint(request, entry, correction);
        var prior = await records.GetReceiptAsync(request.SourceReceiptId, ct);
        if (prior is not null)
        {
            return prior.Fingerprint == fingerprint
                ? new StructuredRecordCaptureResponse(prior.State, prior.RecordId, prior.Number, prior.ErrorCode, prior.RejectedFieldId)
                : new StructuredRecordCaptureResponse("Rejected", null, null, FormRecordErrors.ReplayConflict.Code);
        }

        if (request.PresentationCulture is not (null or "es" or "en"))
            return Reject(request, fingerprint, FormRecordErrors.InvalidResponse.Code);

        if (!await CanCaptureAsync(request, correction, ct))
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
            // Under the lock approvals take: the response is still current, undecided for its author, and the files kept are its own.
            replaced = await records.GetRecordAsync(correction.RecordId, ct);
            if (replaced is null || replaced.DefinitionId != definition.Id) return Reject(request, fingerprint, FormRecordErrors.NotFound.Code);
            var access = await guard.FindAmendAccessAsync(ct);
            var allowed = access is null ? FormDefinitionErrors.Forbidden
                : FormRecordAmendmentGuard.Allows(access, replaced.ActorUserId, replaced.State, replaced.ReplacedByRecordId is not null);
            if (allowed.IsFailure) return Reject(request, fingerprint, allowed.Error.Code);
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
            || offers.Any(offer => offer.Revision <= 0 || offer.Kind is not ("Manual" or "Filters")
                || !schema.Questions.Any(question => question.FieldId == offer.FieldId && question.Key == offer.Key && question.Source?.Catalog == offer.CatalogId)
                || FormCatalogOfferPolicy.Validate(false, offer.Options) is not null
                || FormCatalogOfferPolicy.InvalidNames(offer.CatalogNames) || FormCatalogOfferPolicy.InvalidValues(offer)))
            return Reject(request, fingerprint, FormRecordErrors.InvalidResponse.Code);
        var answers = Normalize(schema, request.Answers, offers);
        if (answers is null)
            return Reject(request, fingerprint, FormRecordErrors.InvalidResponse.Code);

        var selected = answers.Where(answer => schema.Questions.Single(question => question.FieldId == answer.FieldId).Source?.Catalog is not null)
            .SelectMany(answer => (answer.ChoiceIds ?? [answer.ChoiceId!.Value])
                .Select(choiceId => (Answer: answer, ChoiceId: choiceId,
                    Offer: offers.Single(offer => offer.FieldId == answer.FieldId)))).ToArray();
        foreach (var catalogId in selected.Where(value => value.Offer.Kind == "Manual").Select(value => value.Offer.CatalogId).Distinct().Order())
            await records.LockCatalogAsync(catalogId, ct);
        foreach (var value in selected.Where(value => value.Offer.Kind == "Manual"))
            if (!await records.ExistsActiveCatalogItemAsync(value.Offer.CatalogId, value.ChoiceId, ct))
                return Reject(request, fingerprint, FormRecordErrors.CatalogItemInactive.Code, value.Answer.FieldId);

        var catalogFields = await FormCatalogFieldCapturePolicy.CaptureAsync(records,
            selected.Where(value => value.Offer.Kind == "Manual").Select(value => value.ChoiceId).Distinct().ToArray(), ct);

        var origin = await origins.ValidateAsync(request, schema, ct);
        if (origin.IsFailure) return Reject(request, fingerprint, origin.Error.Code);

        var valuesJson = FormDraftPolicy.Serialize(answers.Select(value => new { value.FieldId, value.Value }).ToArray());
        BusinessFormRecord record;
        if (replaced is null)
            record = BusinessFormRecord.Create(definition.Id, version.Id, definition.AllocateRecordNumber(), request.ActorUserId,
                request.SourceReceiptId, request.SubmittedAtUtc, valuesJson, definition.ActiveRuleSetId, request.PresentationCulture, entry);
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
        }
        records.Add(record);
        foreach (var value in answers)
        {
            if (value.Type == "multiChoice")
            {
                for (var position = 0; position < value.ChoiceIds!.Length; position++)
                    records.Add(BusinessFormRecordValue.Create(record.Id, definition.Id, value.FieldId, value.Type,
                        choiceId: value.ChoiceIds[position], position: position));
            }
            else
                records.Add(BusinessFormRecordValue.Create(record.Id, definition.Id, value.FieldId, value.Type,
                    value.Text, value.Decimal, value.Date, value.Boolean, value.ChoiceId));
        }
        var references = new List<FormRecordReferenceRow>();
        foreach (var value in selected)
        {
            var option = value.Offer.Options.Single(option => option.Id == value.ChoiceId);
            var translations = FormRecordReferencePolicy.Capture(option, value.Offer);
            var fieldValuesJson = value.Offer.Kind == "Manual" ? catalogFields.GetValueOrDefault(option.Id) : null;
            records.Add(BusinessFormRecordReference.Create(record.Id, definition.Id, value.Answer.FieldId,
                value.Offer.CatalogId, value.Offer.Revision, option.Id, option.Text, option.Detail, value.Offer.CatalogName, translations, option.Value, value.Offer.ValueType, fieldValuesJson));
            references.Add(new(record.Id, value.Answer.FieldId, value.Offer.CatalogId, value.Offer.Revision, option.Id, option.Text, option.Detail, value.Offer.CatalogName, translations, option.Value, value.Offer.ValueType, fieldValuesJson));
        }
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

    private Result<StructuredRecordCaptureResponse> Reject(StructuredRecordCaptureRequest request, string fingerprint, string code, Guid? rejectedFieldId = null)
    {
        records.Add(BusinessFormCaptureReceipt.Reject(request.SourceReceiptId, request.DefinitionId, request.VersionId,
            fingerprint, code, timeProvider.GetUtcNow().UtcDateTime, rejectedFieldId));
        return new StructuredRecordCaptureResponse("Rejected", null, null, code, rejectedFieldId);
    }

    private static List<NormalizedAnswer>? Normalize(FormVersionSchema schema, IReadOnlyDictionary<string, string> answers, IReadOnlyList<StructuredFormCatalogOffer> offers)
    {
        if (schema.Format != 1 || schema.Questions.Count > 40) return null;
        var questions = schema.Questions;
        if (answers.Keys.Any(key => !questions.Any(question => question.Key == key))) return null;
        var result = new List<NormalizedAnswer>();
        foreach (var question in questions)
        {
            if (question.Type is "photo" or "document") continue;
            if (!answers.TryGetValue(question.Key, out var raw))
            {
                if (question.Required) return null;
                continue;
            }
            if (question.Type == "multiChoice")
            {
                var choices = MultipleChoices(question, raw, offers);
                if (choices is null) return null;
                result.Add(new(question.FieldId, question.Type, null, null, null, null, null, choices));
                continue;
            }
            if (raw.Length == 0)
            {
                if (question.Required) return null;
                continue;
            }
            switch (question.Type)
            {
                case "shortText":
                case "longText":
                    if (raw.Length > question.MaxLength || !StorableText.IsStorable(raw.AsSpan())) return null;
                    result.Add(new(question.FieldId, question.Type, raw, null, null, null, null));
                    break;
                case "decimal":
                    if (!decimal.TryParse(raw, NumberStyles.AllowLeadingSign | NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out var number)
                        || FormScalarPrefillPolicy.CanonicalDecimal(raw) != FormScalarPrefillPolicy.CanonicalDecimal(number.ToString("0.############################", CultureInfo.InvariantCulture))) return null;
                    result.Add(new(question.FieldId, question.Type, null, number, null, null, null));
                    break;
                case "date":
                    if (!DateOnly.TryParseExact(raw, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var date)) return null;
                    result.Add(new(question.FieldId, question.Type, null, null, date, null, null));
                    break;
                case "boolean":
                    if (!bool.TryParse(raw, out var boolean)) return null;
                    result.Add(new(question.FieldId, question.Type, null, null, null, boolean, null));
                    break;
                case "choice":
                    if (!Guid.TryParseExact(raw, "D", out var choice)) return null;
                    if (question.Source?.Catalog is { } catalogId)
                    {
                        var offer = offers.SingleOrDefault(value => value.FieldId == question.FieldId && value.CatalogId == catalogId);
                        if (offer?.Options.Any(option => option.Id == choice) != true) return null;
                    }
                    else if (question.Options?.All(option => option.ChoiceId != choice) != false) return null;
                    result.Add(new(question.FieldId, question.Type, null, null, null, null, choice));
                    break;
                default: return null;
            }
        }
        return result;
    }

    private static Guid[]? MultipleChoices(CanonicalFormField question, string raw, IReadOnlyList<StructuredFormCatalogOffer> offers)
    {
        if (question.Selection is not { } range || range.MinSelected < 0 || range.MaxSelected is < 1 or > 20
            || range.MinSelected > range.MaxSelected || question.Required != (range.MinSelected > 0)) return null;
        try
        {
            using var json = JsonDocument.Parse(raw);
            if (json.RootElement.ValueKind != JsonValueKind.Array) return null;
            var count = json.RootElement.GetArrayLength();
            if (count < range.MinSelected || count > range.MaxSelected) return null;
            var choices = new Guid[count];
            var distinct = new HashSet<Guid>();
            for (var index = 0; index < count; index++)
            {
                var value = json.RootElement[index];
                if (value.ValueKind != JsonValueKind.String || !Guid.TryParseExact(value.GetString(), "D", out var choice)
                    || choice == Guid.Empty || !distinct.Add(choice)) return null;
                if (question.Source?.Catalog is { } catalogId)
                {
                    var offer = offers.SingleOrDefault(offer => offer.FieldId == question.FieldId && offer.CatalogId == catalogId);
                    if (offer?.Options.Any(option => option.Id == choice) != true) return null;
                }
                else if (question.Options?.Any(option => option.ChoiceId == choice) != true) return null;
                choices[index] = choice;
            }
            return choices;
        }
        catch (JsonException) { return null; }
    }

    // An entry's offer is rebuilt by the server on every send, so it stays out: the replay compares what the person sent.
    private static string Fingerprint(StructuredRecordCaptureRequest request, bool entry, FormRecordCorrection? correction)
    {
        var canonical = JsonSerializer.Serialize(new
        {
            request.SourceReceiptId, request.DefinitionId, request.VersionId, request.ActorUserId,
            Answers = request.Answers.OrderBy(pair => pair.Key, StringComparer.Ordinal).Select(pair => new { pair.Key, pair.Value }),
        });
        if (entry) canonical += "app";
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

    private sealed record NormalizedAnswer(Guid FieldId, string Type, string? Text, decimal? Decimal,
        DateOnly? Date, bool? Boolean, Guid? ChoiceId, Guid[]? ChoiceIds = null)
    {
        public object Value => Type switch
        {
            "shortText" or "longText" => Text!, "decimal" => Decimal!.Value, "date" => Date!.Value,
            "boolean" => Boolean!.Value, "choice" => ChoiceId!.Value, "multiChoice" => ChoiceIds!, _ => throw new InvalidOperationException("Unknown form answer type."),
        };
    }
}
