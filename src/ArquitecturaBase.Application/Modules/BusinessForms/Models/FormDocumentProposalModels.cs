using ArquitecturaBase.Application.Models.Assistant;
using ArquitecturaBase.Domain.Modules.BusinessForms;
using ArquitecturaBase.Domain.Results;

namespace ArquitecturaBase.Application.Modules.BusinessForms.Models;

// DOC6, «Armar formulario con la IA» (task 14): a reviewer who designs forms asks the assistant for a form from one unclassified document
// or from a group of similar ones (a topic of «Formularios que faltan»), waits for the proposal like a dictation, edits it and creates the
// draft with the reading on; once that form is published, its documents are read again for it.

/// <summary>Asking for a form: the request's UUID (its repetition answers the same) and either one unclassified document or a topic.</summary>
public sealed record RequestFormDocumentProposalRequest(Guid? RequestId, Guid? DocumentId, Guid? TopicId);

/// <summary>A topic of «Formularios que faltan», by the name it was noted with.</summary>
public sealed record FormReceivedDocumentTopicResponse(Guid Id, string Name)
{
    public override string ToString() => "FormReceivedDocumentTopicResponse { Content = [REDACTED] }";
}

/// <summary>
/// The proposal the assistant closed on, as Home shows one: its Id (what «Crear borrador» names), the capability and version
/// (<c>forms.authoring.propose-form@2</c>) and its content, the form with what each question read in the first document, its reading and a
/// similar library standard.
/// </summary>
public sealed record FormDocumentProposalContentResponse(Guid Id, string Capability, int Version, FormAuthoringDocumentFormProposal Content)
{
    public override string ToString() => "FormDocumentProposalContentResponse { Content = [REDACTED] }";
}

/// <summary>
/// A request for a form while the assistant builds it (<c>pending</c>), once it proposed one (<c>completed</c>, with the proposal) or when it
/// could not (<c>failed</c>, with the code of why: <c>Forms.ReceivedDocument.NoFormProposal</c>, <c>Forms.ReceivedDocument.ProposalNotFound</c>
/// or the engine's own failure code, passed through as it is); the topic of its group, if it was asked for one; its documents, in the order
/// they were attached and as the inbox lists them; and the draft «Crear borrador» created from it, if any.
/// </summary>
public sealed record FormDocumentProposalResponse(Guid RequestId, string State, string? FailureCode, FormReceivedDocumentTopicResponse? Topic,
    IReadOnlyList<FormReceivedDocumentListItemResponse> Documents, FormDocumentProposalContentResponse? Proposal, Guid? DefinitionId)
{
    public override string ToString() => "FormDocumentProposalResponse { Content = [REDACTED] }";
}

/// <summary>
/// «Crear borrador» from the proposal of a request, as the reviewer edited it: the name, prefix and final button text, «Cómo reconocerlo»
/// (the reading goes on) and the questions as the proposal names them, each with its type, whether it is required and its own options or an
/// existing catalog.
/// </summary>
public sealed record CreateFormDocumentDraftRequest(Guid? RequestId, Guid? ProposalId, string? Name, string? Prefix, string? SubmitLabel,
    string? Recognition, IReadOnlyList<CreateFormDocumentDraftQuestion?>? Questions)
{
    public override string ToString() => "CreateFormDocumentDraftRequest { Content = [REDACTED] }";
}

public sealed record CreateFormDocumentDraftQuestion(string? Label, string? Type, bool? Required, IReadOnlyList<string?>? Options, Guid? CatalogId)
{
    public override string ToString() => "CreateFormDocumentDraftQuestion { Content = [REDACTED] }";
}

/// <summary>
/// «Crear borrador» of DOC6 as the inbox hands it to the owner of creating from a proposal (<c>FormDraftProposalService</c>), once it checked
/// the request and that the proposal is the one its operation closed on: who asks, the proposal and its provenance, the form as the reviewer
/// edited it with its «Cómo reconocerlo», and what the same save leaves in the request (the draft it created).
/// </summary>
public sealed record CreateFromDocumentProposalRequest(Guid ActorUserId, AssistantObservedProposal Proposal, AssistantOperationProvenance Provenance,
    FormAuthoringFormProposal Form, FormAuthoringProposedReading Reading, Func<BusinessFormDefinition, CancellationToken, Task<Result>> RecordAsync)
{
    public override string ToString() => "CreateFromDocumentProposalRequest { Content = [REDACTED] }";
}

// A request as it is kept: who asked, in which language and zone, its group's topic (with the name it was noted with), its documents in
// order, its draft and the proposal it came from, and the version its documents were read again for.
public sealed record FormDocumentProposalStoredRow(Guid Id, Guid RequestId, Guid ActorUserId, string Culture, string TimeZoneId, DateTime RequestedAtUtc,
    Guid? TopicId, string? TopicName, IReadOnlyList<Guid> DocumentIds, Guid? DefinitionId, Guid? ProposalId, Guid? RereadVersionId)
{
    public override string ToString() => "FormDocumentProposalStoredRow { Content = [REDACTED] }";
}

/// <summary>
/// What the re-reading has to do: the requests whose form has a published version that reads documents and was not read again for it yet,
/// and the documents of a request whose reading for its form still runs.
/// </summary>
public sealed record FormDocumentRereadDueResponse(IReadOnlyList<Guid> ProposalIds, IReadOnlyList<Guid> DocumentIds);

/// <summary>
/// A request reserved to read its documents again for the published version of its form, with the context of whoever asked: the documents
/// still pending for that version, held for this round.
/// </summary>
public sealed record FormDocumentRereadWorkResponse(Guid ProposalId, Guid DefinitionId, Guid VersionId, Guid ActorUserId, string Culture, string TimeZoneId,
    IReadOnlyList<Guid> DocumentIds)
{
    public override string ToString() => "FormDocumentRereadWorkResponse { Content = [REDACTED] }";
}

/// <summary>
/// What became of one document of a request reserved for a version: read again (no failure), left as it is (a failure that will not pass,
/// or it no longer needs it) or refused for now by the engine (its quota or its capacity, with the time it says).
/// </summary>
public sealed record FormDocumentRereadOutcomeRequest(Guid ProposalId, Guid VersionId, Guid DocumentId, Error? Failure);

/// <summary>
/// Reading one document again for the form built from it, as the server: the form and who asked for the proposal, in their language and
/// zone, the context its extraction is sealed with.
/// </summary>
public sealed record FormReceivedDocumentRereadRequest(Guid DocumentId, Guid DefinitionId, Guid ActorUserId, string Culture, string TimeZoneId);

// A group of similar unclassified documents (DOC6): a pending topic of «Formularios que faltan», how many of its documents are unclassified
// under the page's other filters and the newest of them, up to a request's documents.
public sealed record FormReceivedDocumentGroupRow(Guid TopicId, string TopicName, int Count, IReadOnlyList<Guid> DocumentIds)
{
    public override string ToString() => "FormReceivedDocumentGroupRow { Content = [REDACTED] }";
}

/// <summary>
/// The notice of similar documents (DOC6): three or more unclassified documents that share a topic of «Formularios que faltan», under the
/// page's other filters, with the topic, how many they are and the newest of them.
/// </summary>
public sealed record FormReceivedDocumentGroupResponse(FormReceivedDocumentTopicResponse Topic, int Count, IReadOnlyList<Guid> DocumentIds)
{
    public override string ToString() => "FormReceivedDocumentGroupResponse { Content = [REDACTED] }";
}
