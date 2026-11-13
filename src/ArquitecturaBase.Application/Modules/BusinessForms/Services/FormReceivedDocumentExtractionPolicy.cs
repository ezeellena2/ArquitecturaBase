using ArquitecturaBase.Application.Models.Assistant;
using ArquitecturaBase.Application.Models.StructuredData;
using ArquitecturaBase.Application.Modules.BusinessForms.Channels;
using ArquitecturaBase.Application.Modules.BusinessForms.Interfaces.Persistence;
using ArquitecturaBase.Application.Modules.BusinessForms.Models;
using ArquitecturaBase.Application.Modules.BusinessForms.Resources;
using ArquitecturaBase.Domain.Modules.BusinessForms;

namespace ArquitecturaBase.Application.Modules.BusinessForms.Services;

/// <summary>
/// Changing a received document's form (decision 15): an interpretation of the channel <c>forms-documents</c>, with the reviewer who asked
/// as Actor and without a Subject, offering only the fixed form's read and propose-entry, with the form fixed by the server
/// (<see cref="AssistantChannelContext.FocusDefinitionId"/>) and the reading the document keeps as its attachment, never read again. The
/// context is sealed with the document when it is asked for, so any reviewer reads the operation with it. Its proposal is accepted only
/// from that read and prepared without AI like a registration's, keeping only the fixed form; a form that stopped being active or changed
/// version since has its own reason. Each document is its own channel conversation (DOC6): the readings of different documents, a
/// reviewer's or the server's after publishing a form, never wait for each other.
/// </summary>
internal sealed class FormReceivedDocumentExtractionPolicy(FormReceivedDocumentContentPolicy contents, FormDocumentProposalPolicy proposals,
    IFormReceivedDocumentReader reader, FormDocumentRowPolicy rows)
{
    public const string ChannelId = "forms-documents";
    public const int ContentDays = 7;
    /// <summary>The extraction finished without recognizing the fixed form in the document.</summary>
    public const string Unrecognized = "Forms.ReceivedDocument.Unrecognized";
    /// <summary>The fixed form stopped being active, or published another version, while the extraction ran.</summary>
    public const string FormChanged = "Forms.ReceivedDocument.FormChanged";
    /// <summary>The engine refused the extraction for good while it was read (403): it was dropped.</summary>
    public const string Refused = "Forms.ReceivedDocument.ExtractionRefused";

    /// <summary>VC1-A: a row is mapped again with propose-rows instead of propose-entry, on a sample of its own row.</summary>
    public static AssistantChannelContext Context(Guid actorUserId, string culture, string timeZoneId, Guid definitionId, Guid documentId, bool row = false) =>
        new(ChannelId, actorUserId, null, culture, timeZoneId, new([BusinessFormsPermissions.DocumentsReview], []),
            [new(BusinessFormDocumentAssistantCapabilitySource.FormId, BusinessFormDocumentAssistantCapabilitySource.Version),
                new(row ? BusinessFormDocumentAssistantCapabilitySource.ProposeRowsId : BusinessFormDocumentAssistantCapabilitySource.ProposeEntryId,
                    BusinessFormDocumentAssistantCapabilitySource.Version)],
            [new("assistant_proposal", 1)]) { FocusDefinitionId = definitionId, ChannelConversationId = documentId };

    /// <summary>The context sealed with the extraction that runs (who asked, in which language and zone), or null without one.</summary>
    public static AssistantChannelContext? Sealed(FormReceivedDocumentStoredRow row) =>
        row is { ExtractionDefinitionId: { } definitionId, ExtractionActorUserId: { } actorUserId, ExtractionCulture: { } culture, ExtractionTimeZoneId: { } zone }
            ? Context(actorUserId, culture, zone, definitionId, row.Id, row.RowNumber is not null) : null;

    /// <summary>The operation that extracts the document for the fixed form, or null when it has no reading it can attach.</summary>
    public AssistantOperationRequest? Request(FormReceivedDocumentStoredRow row, Guid requestId, AssistantChannelContext context, DateTime requestedAtUtc)
    {
        if (row.RowNumber is not null) return RowRequest(row, requestId, context, requestedAtUtc);
        if (contents.ReadReading(row.Id, row.ProtectedReading) is not { } reading || Kind(row.MediaType) is not { } kind
            || row is not { StorageKey: { } key, Sha256: { } sha256, Size: { } size }) return null;
        var expiresAtUtc = requestedAtUtc.AddDays(ContentDays);
        var document = new StructuredDocument(row.FileName, kind, row.MediaType, size, sha256, row.Pages, reading);
        try
        {
            var attachment = AssistantAttachmentInput.ForDocument(key, row.FileName, expiresAtUtc, document);
            return new AssistantOperationRequest(requestId, context, FormReceivedDocumentTexts.ExtractionRequest(context.Culture), requestedAtUtc, expiresAtUtc)
                { Attachment = attachment };
        }
        catch (ArgumentException) { return null; }
    }

    /// <summary>
    /// What a completed extraction closed on, for the fixed form: its own read, the fixed form as the one candidate, prepared on the form's
    /// active version with the same document excluded from the repetitions; or why not (nothing recognized for that form, or the form changed).
    /// </summary>
    public async Task<FormReceivedDocumentExtractionOutcome> PrepareAsync(AssistantOperationResponse operation, FormReceivedDocumentStoredRow row,
        Guid definitionId, CancellationToken ct)
    {
        if (row.RowNumber is not null) return await PrepareRowAsync(operation, row, definitionId, ct);
        var proposal = operation.Proposals.FirstOrDefault(value => value.Capability.Id == BusinessFormDocumentAssistantCapabilitySource.ProposeEntryId)
            ?? operation.Proposal;
        if (proposal is null || !FormDocumentObservationPolicy.Authentic(proposal, out var entry, out var read)
            || read is not { Capability: BusinessFormDocumentAssistantCapabilitySource.FormId }) return new(null, null, Unrecognized);
        var focused = entry with
        {
            Outcome = FormDocumentEntryProposal.Sure,
            Candidates = entry.Candidates.Where(candidate => candidate.DefinitionId == definitionId).Take(1).ToArray(),
        };
        if (focused.Candidates is not [var candidate]) return new(null, null, Unrecognized);
        var form = await reader.FindActiveFormAsync(definitionId, ct);
        if (form is null || form.VersionId != candidate.VersionId) return new(null, null, FormChanged);
        if (row.Sha256 is not { } sha256) return new(null, null, Unrecognized);
        var prepared = await proposals.PrepareAsync(focused, sha256, row.Id, ct);
        return prepared.Candidates.Count == 1 ? new(prepared, form, null) : new(null, null, FormChanged);
    }

    // VC1-A: a row is read again from what it keeps, never from its spreadsheet: its headers and its cells as a sample to load of one row of data,
    // under the copy of its spreadsheet.
    private AssistantOperationRequest? RowRequest(FormReceivedDocumentStoredRow row, Guid requestId, AssistantChannelContext context, DateTime requestedAtUtc)
    {
        if (contents.ReadRow(row.Id, row.ProtectedReading) is not { } reading || row is not { StorageKey: { } key, Sha256: { } sha256, Size: { } size }
            || FormReceivedDocumentRowPolicy.Format(row.MediaType) is not { } format) return null;
        var sheet = new FormSpreadsheet(reading.Sheet, sha256, [new(reading.HeaderNumber, [.. reading.Headers.Select(text => new FormSpreadsheetCell(text))]),
            new(reading.Number, reading.SheetRow().Cells)]);
        var name = FormAttachmentPreparationPolicy.FileName(row.FileName);
        var prepared = FormAttachmentPreparationPolicy.PrepareRows(name, format == FormSpreadsheetFormat.Csv ? StructuredSpreadsheetKind.Csv : StructuredSpreadsheetKind.Xlsx,
            size, [sheet]);
        if (prepared.Spreadsheet is not { } sample) return null;
        var expiresAtUtc = requestedAtUtc.AddDays(ContentDays);
        return new AssistantOperationRequest(requestId, context, FormReceivedDocumentTexts.ExtractionRequest(context.Culture), requestedAtUtc, expiresAtUtc)
        {
            Attachment = new AssistantAttachmentInput(key, name, row.MediaType, size, sha256, expiresAtUtc, sample),
        };
    }

    // VC1-A: a row's change of form closes on propose-rows from the fixed form's read (never a candidates read, like a document), armed again
    // from what the row keeps and in its person's language, like at its registration; the fixed form is the one candidate.
    private async Task<FormReceivedDocumentExtractionOutcome> PrepareRowAsync(AssistantOperationResponse operation, FormReceivedDocumentStoredRow row,
        Guid definitionId, CancellationToken ct)
    {
        var proposal = operation.Proposals.FirstOrDefault(value => value.Capability.Id == BusinessFormDocumentAssistantCapabilitySource.ProposeRowsId)
            ?? operation.Proposal;
        if (proposal is null || !FormDocumentRowCodec.Authentic(proposal, out var mapping, out var read)
            || read is not { Capability: BusinessFormDocumentAssistantCapabilitySource.FormId } || contents.ReadRow(row.Id, row.ProtectedReading) is not { } reading)
            return new(null, null, Unrecognized);
        var focused = mapping with { Forms = [.. mapping.Forms.Where(form => form.DefinitionId == definitionId).Take(1)] };
        if (focused.Forms is not [var mapped]) return new(null, null, Unrecognized);
        var form = await reader.FindActiveFormAsync(definitionId, ct);
        if (form is null || form.VersionId != mapped.VersionId) return new(null, null, FormChanged);
        var plan = await rows.PrepareAsync(focused, [reading.SheetRow()], reading.Culture, ct);
        return plan.Rows is [{ Candidate: { } candidate } armed]
            ? new(new FormDocumentPreparedProposal(FormDocumentEntryProposal.Sure, plan.DocumentType, [candidate]) { Repetition = armed.Repetition }, form, null)
            : new(null, null, Unrecognized);
    }

    public byte[] Protect(Guid documentId, FormDocumentPreparedProposal proposal) => contents.ProtectProposal(documentId, proposal);

    public StructuredDocumentReading? Reading(Guid documentId, byte[]? value) => contents.ReadReading(documentId, value);

    public static StructuredDocumentKind? Kind(string mediaType) => mediaType switch
    {
        "application/pdf" => StructuredDocumentKind.Pdf,
        "image/jpeg" => StructuredDocumentKind.Jpeg,
        "image/png" => StructuredDocumentKind.Png,
        "application/vnd.openxmlformats-officedocument.wordprocessingml.document" => StructuredDocumentKind.Docx,
        _ => null,
    };
}

/// <summary>What a finished extraction left for the fixed form: its prepared proposal and the form, or the code of why not.</summary>
internal sealed record FormReceivedDocumentExtractionOutcome(FormDocumentPreparedProposal? Proposal, FormDocumentFormRow? Form, string? FailureCode)
{
    public override string ToString() => "FormReceivedDocumentExtractionOutcome { Content = [REDACTED] }";
}
