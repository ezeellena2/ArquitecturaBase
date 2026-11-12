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
/// version since has its own reason.
/// </summary>
internal sealed class FormReceivedDocumentExtractionPolicy(FormReceivedDocumentContentPolicy contents, FormDocumentProposalPolicy proposals,
    IFormReceivedDocumentReader reader)
{
    public const string ChannelId = "forms-documents";
    public const int ContentDays = 7;
    /// <summary>The extraction finished without recognizing the fixed form in the document.</summary>
    public const string Unrecognized = "Forms.ReceivedDocument.Unrecognized";
    /// <summary>The fixed form stopped being active, or published another version, while the extraction ran.</summary>
    public const string FormChanged = "Forms.ReceivedDocument.FormChanged";
    /// <summary>The engine refused the extraction for good while it was read (403): it was dropped.</summary>
    public const string Refused = "Forms.ReceivedDocument.ExtractionRefused";

    public static AssistantChannelContext Context(Guid actorUserId, string culture, string timeZoneId, Guid definitionId) =>
        new(ChannelId, actorUserId, null, culture, timeZoneId, new([BusinessFormsPermissions.DocumentsReview], []),
            [new(BusinessFormDocumentAssistantCapabilitySource.FormId, BusinessFormDocumentAssistantCapabilitySource.Version),
                new(BusinessFormDocumentAssistantCapabilitySource.ProposeEntryId, BusinessFormDocumentAssistantCapabilitySource.Version)],
            [new("assistant_proposal", 1)]) { FocusDefinitionId = definitionId };

    /// <summary>The context sealed with the extraction that runs (who asked, in which language and zone), or null without one.</summary>
    public static AssistantChannelContext? Sealed(FormReceivedDocumentStoredRow row) =>
        row is { ExtractionDefinitionId: { } definitionId, ExtractionActorUserId: { } actorUserId, ExtractionCulture: { } culture, ExtractionTimeZoneId: { } zone }
            ? Context(actorUserId, culture, zone, definitionId) : null;

    /// <summary>The operation that extracts the document for the fixed form, or null when it has no reading it can attach.</summary>
    public AssistantOperationRequest? Request(FormReceivedDocumentStoredRow row, Guid requestId, AssistantChannelContext context, DateTime requestedAtUtc)
    {
        if (contents.ReadReading(row.Id, row.ProtectedReading) is not { } reading || Kind(row.MediaType) is not { } kind) return null;
        var expiresAtUtc = requestedAtUtc.AddDays(ContentDays);
        var document = new StructuredDocument(row.FileName, kind, row.MediaType, row.Size, row.Sha256, row.Pages, reading);
        try
        {
            var attachment = AssistantAttachmentInput.ForDocument(row.StorageKey, row.FileName, expiresAtUtc, document);
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
        var prepared = await proposals.PrepareAsync(focused, row.Sha256, row.Id, ct);
        return prepared.Candidates.Count == 1 ? new(prepared, form, null) : new(null, null, FormChanged);
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
