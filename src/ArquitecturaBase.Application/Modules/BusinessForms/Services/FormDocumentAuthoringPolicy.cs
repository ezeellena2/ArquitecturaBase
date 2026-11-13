using ArquitecturaBase.Application.Models.Assistant;
using ArquitecturaBase.Application.Models.Identity;
using ArquitecturaBase.Application.Models.StructuredData;
using ArquitecturaBase.Application.Modules.BusinessForms.Channels;
using ArquitecturaBase.Application.Modules.BusinessForms.Interfaces.Persistence;
using ArquitecturaBase.Application.Modules.BusinessForms.Models;
using ArquitecturaBase.Application.Modules.BusinessForms.Resources;
using ArquitecturaBase.Domain.Modules.BusinessForms;
using ArquitecturaBase.Domain.Modules.BusinessForms.Documents;
using ArquitecturaBase.Domain.Results;

namespace ArquitecturaBase.Application.Modules.BusinessForms.Services;

/// <summary>
/// «Armar formulario con la IA» (DOC6, decision 17): an interpretation of the channel <c>forms-documents</c> with the reviewer who designs
/// forms as Actor and without a Subject, in a channel conversation of its own (its request), offering the authoring reads, the library read
/// and <c>propose-form@2</c>, closed by the terminal that selects one observed proposal. Its documents are the one unclassified document asked
/// for, or the unclassified documents of the topic that no draft was built from yet, the newest first, up to a request's; only those whose
/// reading and copy can go. Its attachment is the readings of up to five of them, the first one with its copy and half of the budget, each
/// cut by the server to its share and the ones that do not fit left out. It is asked and read with «Revisar documentos» and «Diseñar
/// formularios».
/// </summary>
internal sealed class FormDocumentAuthoringPolicy(FormReceivedDocumentAccessGuard guard, IFormDocumentProposalReader proposals,
    IFormReceivedDocumentReader documents, FormReceivedDocumentContentPolicy contents)
{
    public const string ChannelId = FormReceivedDocumentExtractionPolicy.ChannelId;
    public const int ContentDays = FormReceivedDocumentExtractionPolicy.ContentDays;
    /// <summary>The readings that go to the assistant: the first one and up to four more.</summary>
    public const int MaxReadings = 1 + AssistantAttachmentInput.MaximumMoreDocuments;
    /// <summary>The assistant finished without proposing a form.</summary>
    public const string NoProposal = "Forms.ReceivedDocument.NoFormProposal";
    // Its answers carry the inbox's documents and what the assistant read in them: seeing and reviewing the inbox, and designing forms.
    private static readonly string[] Required = [BusinessFormsPermissions.DocumentsRead, BusinessFormsPermissions.DocumentsReview,
        BusinessFormsPermissions.Read, BusinessFormsPermissions.Manage];
    public static readonly AssistantCapabilityReference ProposeForm = new(BusinessFormAuthoringAssistantCapabilitySource.ProposeFormId,
        BusinessFormAuthoringAssistantCapabilitySource.DocumentFormVersion);
    public static readonly IReadOnlyList<AssistantCapabilityReference> Capabilities =
    [
        new(BusinessFormAuthoringAssistantCapabilitySource.FormsId, BusinessFormAuthoringAssistantCapabilitySource.Version),
        new(BusinessFormAuthoringAssistantCapabilitySource.CatalogsId, BusinessFormAuthoringAssistantCapabilitySource.Version),
        new(BusinessFormLibraryAssistantCapabilitySource.StandardsId, BusinessFormLibraryAssistantCapabilitySource.Version),
        ProposeForm,
    ];

    public static AssistantChannelContext Context(Guid actorUserId, string culture, string timeZoneId, Guid requestId) =>
        new(ChannelId, actorUserId, null, culture, timeZoneId, new(Required, []), Capabilities, [new("assistant_proposal", 1)])
            { ChannelConversationId = requestId };

    /// <summary>The context a request was asked with: who asked, in which language and zone, and its request as its conversation.</summary>
    public static AssistantChannelContext Context(FormDocumentProposalStoredRow row) => Context(row.ActorUserId, row.Culture, row.TimeZoneId, row.RequestId);

    /// <summary>«Ver documentos recibidos», «Revisar documentos» and «Diseñar formularios» (read and manage forms), of an active account.</summary>
    public async Task<Result<UserAccount>> CheckAsync(CancellationToken ct)
    {
        var actor = await guard.CheckAsync(BusinessFormsPermissions.DocumentsReview, ct);
        if (actor.IsFailure) return actor;
        return await HasAsync(actor.Value.Id, ct) ? actor : FormDefinitionErrors.Forbidden;
    }

    public async Task<bool> HasAsync(Guid actorUserId, CancellationToken ct)
    {
        foreach (var permission in Required)
            if (!await guard.HasAsync(actorUserId, permission, ct)) return false;
        return true;
    }

    /// <summary>The one document asked for, unclassified, with a reading and a copy that can go.</summary>
    public async Task<Result<IReadOnlyList<FormReceivedDocumentStoredRow>>> DocumentAsync(Guid documentId, CancellationToken ct)
    {
        if (await documents.FindAsync(documentId, ct) is not { } row) return FormReceivedDocumentErrors.NotFound;
        if (row.State != FormReceivedDocumentState.Unclassified) return FormReceivedDocumentErrors.StateConflict;
        return Document(row) is null ? FormReceivedDocumentErrors.ReadingUnavailable : Result.Success<IReadOnlyList<FormReceivedDocumentStoredRow>>([row]);
    }

    /// <summary>
    /// A group: the unclassified documents of the topic that no draft was built from yet, the newest first, up to a request's, only those
    /// whose reading and copy can go.
    /// </summary>
    public async Task<Result<IReadOnlyList<FormReceivedDocumentStoredRow>>> GroupAsync(Guid topicId, CancellationToken ct)
    {
        var ids = await proposals.ListTopicDocumentIdsAsync(topicId, FormDocumentProposal.MaxDocuments, ct);
        var found = ids.Count == 0 ? [] : await proposals.ListDocumentsAsync(ids, ct);
        var group = ids.Take(FormDocumentProposal.MaxDocuments).Select(id => found.FirstOrDefault(row => row.Id == id))
            .Where(row => row is { State: FormReceivedDocumentState.Unclassified } && Document(row) is not null).OfType<FormReceivedDocumentStoredRow>().ToArray();
        return group.Length == 0 ? FormReceivedDocumentErrors.GroupUnavailable : Result.Success<IReadOnlyList<FormReceivedDocumentStoredRow>>(group);
    }

    /// <summary>
    /// The operation that asks for the form: the request's text in the reviewer's language and the readings of the first documents, the first
    /// one with its copy; null when not even the first can go.
    /// </summary>
    public AssistantOperationRequest? Request(Guid requestId, AssistantChannelContext context, IReadOnlyList<FormReceivedDocumentStoredRow> rows,
        DateTime requestedAtUtc)
    {
        var readings = rows.Select(row => (Row: row, Document: Document(row))).Where(pair => pair.Document is not null).Take(MaxReadings).ToArray();
        if (readings is not [var first, .. var more] || first.Row.StorageKey is not { } key) return null;
        var expiresAtUtc = requestedAtUtc.AddDays(ContentDays);
        try
        {
            var attachment = AssistantAttachmentInput.ForDocument(key, first.Row.FileName, expiresAtUtc, first.Document!,
                more.Length == 0 ? null : more.Select(pair => pair.Document!).ToArray());
            return new AssistantOperationRequest(requestId, context, FormReceivedDocumentTexts.FormProposalRequest(context.Culture), requestedAtUtc, expiresAtUtc)
                { Attachment = attachment };
        }
        catch (ArgumentException) { return null; }
    }

    /// <summary>The form proposal an operation closed on: propose-form@2, the only proposal the channel offers.</summary>
    public static AssistantObservedProposal? Proposal(AssistantOperationResponse operation) =>
        operation.Proposals.FirstOrDefault(IsFormProposal) ?? (operation.Proposal is { } proposal && IsFormProposal(proposal) ? proposal : null);

    private static bool IsFormProposal(AssistantObservedProposal proposal) =>
        proposal.Capability == ProposeForm && proposal.Payload is FormAuthoringDocumentFormProposal;

    // A document goes with its reading, a kind the assistant reads and its copy.
    private StructuredDocument? Document(FormReceivedDocumentStoredRow row) =>
        contents.ReadReading(row.Id, row.ProtectedReading) is { } reading && FormReceivedDocumentExtractionPolicy.Kind(row.MediaType) is { } kind
        && row is { StorageKey: not null, Sha256: { } sha256, Size: { } size }
            ? new StructuredDocument(row.FileName, kind, row.MediaType, size, sha256, row.Pages, reading) : null;
}
