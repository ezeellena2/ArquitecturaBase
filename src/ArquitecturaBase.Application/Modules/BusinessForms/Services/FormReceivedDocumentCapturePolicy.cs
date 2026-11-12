using ArquitecturaBase.Application.Models.Identity;
using ArquitecturaBase.Application.Models.StructuredData;
using ArquitecturaBase.Application.Modules.BusinessForms.Interfaces.Services;
using ArquitecturaBase.Application.Modules.BusinessForms.Models;
using ArquitecturaBase.Domain.Modules.BusinessForms;
using ArquitecturaBase.Domain.Results;

namespace ArquitecturaBase.Application.Modules.BusinessForms.Services;

/// <summary>
/// Passing a received document to its form (decision 15) through the common capture, as the app loads a response in the name of whoever
/// sent it: their catalogs' filters and their account's language, the version's questions, the document attached to the first question of
/// a document (or of a photo, for a photo) that accepts it, and an origin with the reviewer as operator, the document and the revision the
/// reviewer saw, and what the reading proposed for that same version (each value only if it still validates) and the form included, so
/// «Cómo viene leyendo» counts its corrections. A catalog choice is filled by the server and never proposed.
/// </summary>
internal sealed class FormReceivedDocumentCapturePolicy(FormEntryRecorder entries, FormEntryCatalogPolicy catalogs, FormEntryVersionPolicy describer,
    IBusinessFormRecordCaptureService capture, FormReceivedDocumentCopyPolicy copies, FormReceivedDocumentContentPolicy contents)
{
    public const string SourceId = "documents";

    public async Task<Result<FormReceivedDocumentCapture>> PrepareAsync(FormReceivedDocumentStoredRow row, FormEntryDefinitionRow definition,
        FormEntryVersionRow version, Guid reviewerUserId, PassFormReceivedDocumentRequest request, CancellationToken ct)
    {
        var schema = FormDraftPolicy.Read<FormVersionSchema>(version.SchemaJson);
        var entry = entries.Prepare(definition.DefinitionId, version, schema, row.SenderUserId,
            new SubmitFormEntryRequest(request.RequestId!.Value, version.VersionId, request.Answers, []));
        if (entry.IsFailure) return entry.Error;
        var image = row.MediaType is "image/jpeg" or "image/png";
        var question = schema.Questions.FirstOrDefault(value => value.Files is { MaxFiles: >= 1 } && value.Repeated != true && value.Type == (image ? "photo" : "document"))
            ?? (image ? schema.Questions.FirstOrDefault(value => value.Files is { MaxFiles: >= 1 } && value.Repeated != true && value.Type == "document") : null);
        List<StructuredRecordFileInput> files = [];
        if (question is not null)
        {
            var content = await copies.ReadAsync(row.StorageKey, row.Sha256, row.Size, ct);
            if (content is null) return FormReceivedDocumentErrors.FileUnavailable;
            files.Add(new(question.FieldId, 0, SourceId, question.Type, row.FileName, row.MediaType, row.Sha256, content));
        }
        var proposed = Proposed(row, schema, version.VersionId);
        var origin = new StructuredRecordCaptureOrigin(StructuredRecordSenderKind.Manual, reviewerUserId, null, null, proposed,
            proposed.Select(value => value.FieldId).ToArray()) { Document = row.Id, DocumentRevision = request.ExpectedRevision };
        return new FormReceivedDocumentCapture(entry.Value.Request with { Files = files, Origin = origin }, entry.Value.Chosen, entry.Value.Items, schema,
            FormEntryRecorder.Group(schema));
    }

    public Task<StructuredRecordCaptureResponse?> FindExistingAsync(FormReceivedDocumentCapture prepared, CancellationToken ct) =>
        capture.FindExistingDocumentAsync(prepared.Request, ct);

    public async Task<Result<StructuredRecordCaptureResponse>> CaptureAsync(FormReceivedDocumentCapture prepared, FormEntryDefinitionRow definition,
        FormEntryVersionRow version, UserAccount sender, CancellationToken ct)
    {
        var culture = await describer.CultureAsync(definition.DefinitionId, version, prepared.Schema, sender.Culture, ct);
        var offers = await catalogs.OffersAsync(sender.Id, prepared.Schema, prepared.Chosen, culture, version.Culture, ct, prepared.Items);
        if (offers.IsFailure) return offers.Error;
        return await capture.CaptureDocumentAsync(prepared.Request with
        {
            Catalogs = offers.Value.Count == 0 ? null : offers.Value, PresentationCulture = culture,
        }, ct);
    }

    public Task<Result<FormEntrySubmitResponse>> OutcomeAsync(string prefix, StructuredRecordCaptureResponse captured, Guid? group, CancellationToken ct) =>
        entries.OutcomeAsync(prefix, captured, group, ct);

    // What the reading proposed for this same version, as the capture keeps it: the read values of its questions that still validate, never
    // a catalog choice; a proposal read for another version proposes nothing, so it never rejects the response.
    private StructuredRecordProposedValue[] Proposed(FormReceivedDocumentStoredRow row, FormVersionSchema schema, Guid versionId)
    {
        var candidate = contents.ReadProposal(row.Id, row.ProtectedProposal)?.Candidates
            .FirstOrDefault(value => value.DefinitionId == row.DefinitionId && value.VersionId == versionId);
        if (candidate is null) return [];
        var proposed = new List<StructuredRecordProposedValue>();
        foreach (var value in candidate.Values)
        {
            if (value.Value is not { } text || value.Mark == FormDocumentMarks.Completed || proposed.Count == 40) continue;
            var question = schema.Questions.SingleOrDefault(item => item.FieldId == value.FieldId && item.Type == value.Type && item.Source?.Catalog is null);
            if (question is null || FormScalarPrefillPolicy.Normalize(Project(question), text, []) is not { IsSuccess: true } normalized) continue;
            proposed.Add(new(value.FieldId, value.Type, normalized.Value));
        }
        return proposed.ToArray();
    }

    private static StructuredFormQuestion Project(CanonicalFormField field) => new(field.FieldId, field.Key, field.Type, field.Label, field.Help,
        field.Required, field.MaxLength, field.Options?.Select(option => new StructuredFormChoice(option.ChoiceId, option.Label)).ToArray(), null);
}

/// <summary>A received document's capture request with the options each answer chose, the version's questions and its block.</summary>
internal sealed record FormReceivedDocumentCapture(StructuredRecordCaptureRequest Request, IReadOnlyDictionary<Guid, IReadOnlyList<Guid>> Chosen,
    IReadOnlyList<IReadOnlyDictionary<Guid, Guid>>? Items, FormVersionSchema Schema, Guid? Group)
{
    public override string ToString() => "FormReceivedDocumentCapture { Content = [REDACTED] }";
}
