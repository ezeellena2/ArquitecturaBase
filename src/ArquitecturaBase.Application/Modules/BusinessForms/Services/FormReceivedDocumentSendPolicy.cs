using ArquitecturaBase.Application.Models.StructuredData;
using ArquitecturaBase.Application.Modules.BusinessForms.Interfaces.Persistence;
using ArquitecturaBase.Application.Modules.BusinessForms.Models;
using ArquitecturaBase.Domain.Modules.BusinessForms.Documents;

namespace ArquitecturaBase.Application.Modules.BusinessForms.Services;

/// <summary>
/// The form a person chose for their document, prepared to send (DOC3): the candidate of its proposal on the same active version, with
/// the catalogs offered to that person (a form that cannot be sent to them says so), what was read as initial values (a catalog option
/// only when it is offered to them), the mark of each datum and the first question of a document, or of a photo for a photo, that the file
/// goes to. It only reads; the channel compiles and sends.
/// </summary>
internal sealed class FormReceivedDocumentSendPolicy(IFormReceivedDocumentReader reader, FormReceivedDocumentContentPolicy contents, FormCatalogOptionsPolicy options)
{
    /// <summary>The candidate of its stored proposal that the person chose for their proposed document, or null when it is not one of them.</summary>
    public async Task<FormReceivedDocumentChoice?> ChoiceAsync(StructuredDocumentSendRequest request, CancellationToken ct)
    {
        if (request.DocumentId == Guid.Empty || request.DefinitionId == Guid.Empty || request.RecipientUserId == Guid.Empty
            || request.Culture is not ("es" or "en")) return null;
        var row = await reader.FindAsync(request.DocumentId, ct);
        if (row is null || row.ChannelId != request.ChannelId || row.SourceId != request.SourceId || row.SenderUserId != request.RecipientUserId
            || row.State != FormReceivedDocumentState.Proposed) return null;
        var candidate = contents.ReadProposal(row.Id, row.ProtectedProposal)?.Candidates.FirstOrDefault(value => value.DefinitionId == request.DefinitionId);
        return candidate is null ? null : new(row, candidate);
    }

    public async Task<StructuredDocumentSendPreparation> PrepareAsync(StructuredDocumentSendRequest request, CancellationToken ct)
    {
        var unavailable = new StructuredDocumentSendPreparation(StructuredDocumentSendState.Unavailable);
        if (await ChoiceAsync(request, ct) is not { Row: var row, Candidate: var candidate }) return unavailable;
        var form = await reader.FindActiveFormAsync(request.DefinitionId, ct);
        if (form is null || form.AppOnly || form.VersionId != candidate.VersionId) return unavailable;
        var schema = FormDraftPolicy.Read<FormVersionSchema>(form.SchemaJson);
        var prepared = await options.PrepareAsync(request.RecipientUserId, request.Culture, schema, form.Culture, ct);
        if (!prepared.CanSend) return new StructuredDocumentSendPreparation(StructuredDocumentSendState.NotSendable);
        var fields = new List<StructuredFormPrefillField>();
        var marks = new List<StructuredDocumentFieldMark>();
        foreach (var value in candidate.Values)
        {
            if (schema.Questions.SingleOrDefault(question => question.FieldId == value.FieldId) is not { } question) continue;
            var offered = question.Source?.Catalog is not { } catalogId || value.Value is { } chosen
                && prepared.Catalogs.Any(offer => offer.FieldId == question.FieldId && offer.CatalogId == catalogId
                    && offer.Options.Any(option => option.Id.ToString("D") == chosen));
            if (value.Value is null || !offered)
            {
                marks.Add(new(question.FieldId, StructuredDocumentMarkKind.NotRead));
                continue;
            }
            if (Mark(value) is { } mark) marks.Add(new(question.FieldId, mark, value.ErrorCode));
            fields.Add(new(question.FieldId, question.Key, question.Type, FormRecordPresentationPolicy.Text(question.Label, request.Culture),
                value.Value, FormReceivedDocumentReplyPolicy.Display(value, request.Culture) ?? value.Value));
        }
        var image = row.MediaType is "image/jpeg" or "image/png";
        var documentField = schema.Questions.FirstOrDefault(question => question.Files is { MaxFiles: >= 1 } && question.Repeated != true
            && question.Type == (image ? "photo" : "document"))
            ?? (image ? schema.Questions.FirstOrDefault(question => question.Files is { MaxFiles: >= 1 } && question.Repeated != true && question.Type == "document") : null);
        var selection = new StructuredFormSelectionRow(form.DefinitionId, form.VersionId, form.VersionNumber, form.Prefix,
            FormDraftPolicy.Read<Dictionary<string, string>>(form.NameJson), form.QuestionCount, FormDraftPolicy.Read<Dictionary<string, string>>(form.SubmitLabelJson),
            schema.Questions.Any(question => question.Source?.Catalog is not null)) { DefinitionRevision = form.DefinitionRevision };
        return new StructuredDocumentSendPreparation(StructuredDocumentSendState.Ready)
        {
            Prefill = new(selection, form.DefinitionRevision, fields, prepared.Catalogs, row.DocumentType ?? "", prepared.Culture ?? request.Culture),
            Marks = marks,
            DocumentFieldId = documentField?.FieldId,
        };
    }

    private static StructuredDocumentMarkKind? Mark(FormDocumentPreparedValue value) => value.Mark switch
    {
        FormDocumentMarks.Doubtful => StructuredDocumentMarkKind.Doubtful,
        FormDocumentMarks.Invalid => StructuredDocumentMarkKind.Invalid,
        FormDocumentMarks.Completed => StructuredDocumentMarkKind.Completed,
        _ => null,
    };
}

/// <summary>A proposed document and the candidate of its stored proposal that its person chose.</summary>
internal sealed record FormReceivedDocumentChoice(FormReceivedDocumentStoredRow Row, FormDocumentPreparedCandidate Candidate)
{
    public override string ToString() => "FormReceivedDocumentChoice { Content = [REDACTED] }";
}
